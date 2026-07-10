using System.Security.Cryptography;
using System.Net;
using CraftoraApi.Data;
using CraftoraApi.DTOs.Auth;
using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Google.Apis.Auth;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace CraftoraApi.Services;

public sealed class AuthService : IAuthService
{
    private const int OtpExpirationMinutes = 5;
    private const int DefaultRefreshTokenExpireDays = 30;
    private const int MaxFailedLoginAttempts = 5;
    private const int BruteForceWindowMinutes = 30;
    private const int AccessTokenBlacklistMinutes = 15;

    private readonly AppDbContext _dbContext;
    private readonly IDistributedCache _cache;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IJwtProvider _jwtProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AppDbContext dbContext,
        IDistributedCache cache,
        IPublishEndpoint publishEndpoint,
        IJwtProvider jwtProvider,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _publishEndpoint = publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));
        _jwtProvider = jwtProvider ?? throw new ArgumentNullException(nameof(jwtProvider));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> RegisterAsync(RegisterDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var email = NormalizeEmail(dto.Email);
        var emailExists = await _dbContext.Users.AnyAsync(user => user.Email == email);
        if (emailExists)
        {
            throw new ConflictException("Bu e-posta adresi zaten kayıtlı.");
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
        var user = new User
        {
            Email = email,
            FullName = dto.FullName.Trim(),
            Role = UserRole.User,
            AuthProvider = "email",
            PasswordHash = passwordHash,
            IsEmailVerified = false,
            IsActive = true
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var otpCode = GenerateOtpCode();
        await _cache.SetStringAsync(
            GetOtpCacheKey(email),
            otpCode,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(OtpExpirationMinutes)
            });

        await _publishEndpoint.Publish(new SendEmailCommand(
            To: email,
            Subject: "Craftora e-posta dogrulama kodu",
            Body: BuildOtpEmailBody(user.FullName, otpCode),
            IsHtml: true));

        _logger.LogInformation("OTP email command published. UserId: {UserId}, Email: {Email}", user.Id, email);

        return "Kayıt başarılı. Lütfen e-posta adresinize gönderilen doğrulama kodunu giriniz.";
    }

    public async Task<string> VerifyEmailAsync(OtpDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var email = NormalizeEmail(dto.Email);
        var cacheKey = GetOtpCacheKey(email);
        var cachedOtpCode = await _cache.GetStringAsync(cacheKey);

        if (string.IsNullOrWhiteSpace(cachedOtpCode) || cachedOtpCode != dto.OtpCode)
        {
            throw new ValidationException("Geçersiz veya süresi dolmuş doğrulama kodu.");
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(user => user.Email == email);
        if (user is null)
        {
            throw new NotFoundException("Kullanıcı bulunamadı.");
        }

        user.IsEmailVerified = true;
        await _dbContext.SaveChangesAsync();
        await _cache.RemoveAsync(cacheKey);

        return "E-posta adresiniz başarıyla doğrulandı.";
    }

    public async Task<TokenDto> LoginAsync(LoginDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var email = NormalizeEmail(dto.Email);
        var bruteForceKey = $"brute_force:{email}";
        var failedAttemptValue = await _cache.GetStringAsync(bruteForceKey);
        var failedAttemptCount = int.TryParse(failedAttemptValue, out var parsedAttemptCount)
            ? parsedAttemptCount
            : 0;

        if (failedAttemptCount >= MaxFailedLoginAttempts)
        {
            throw new UnauthorizedException("Çok fazla hatalı deneme yaptınız. Lütfen 30 dakika sonra tekrar deneyin.");
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(user => user.Email == email);

        if (user is null || string.IsNullOrWhiteSpace(user.PasswordHash) ||
            !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
        {
            failedAttemptCount++;

            await _cache.SetStringAsync(
                bruteForceKey,
                failedAttemptCount.ToString(),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(BruteForceWindowMinutes)
                });

            throw new UnauthorizedException("E-posta veya şifre hatalı.");
        }

        await _cache.RemoveAsync(bruteForceKey);

        if (user.IsEmailVerified != true)
        {
            throw new UnauthorizedException("Lütfen önce e-posta adresinizi doğrulayın.");
        }

        await PromoteShopOwnerToSellerAsync(user);

        return await IssueTokensAsync(user);
    }

    public async Task<TokenDto> GoogleLoginAsync(string idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            throw new UnauthorizedException("Geçersiz Google token.");
        }

        var clientId = _configuration["OAuth:Google:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("Google ClientId not found in appsettings.");
        }

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(
                idToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { clientId }
                });
        }
        catch (Exception exception) when (exception is InvalidJwtException or ArgumentException)
        {
            throw new UnauthorizedException("Geçersiz Google token.");
        }

        if (string.IsNullOrWhiteSpace(payload.Email))
        {
            throw new UnauthorizedException("Google hesabından e-posta bilgisi alınamadı.");
        }

        var email = NormalizeEmail(payload.Email);
        var user = await _dbContext.Users.FirstOrDefaultAsync(user => user.Email == email);

        if (user is null)
        {
            user = new User
            {
                Email = email,
                FullName = payload.Name,
                Role = UserRole.User,
                AuthProvider = "google",
                ProviderId = payload.Subject,
                IsEmailVerified = true,
                IsActive = true
            };

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();
        }
        else
        {
            user.IsEmailVerified = true;
            user.ProviderId ??= payload.Subject;
        }

        await PromoteShopOwnerToSellerAsync(user);

        return await IssueTokensAsync(user);
    }

    public async Task<TokenDto> RefreshTokenAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new UnauthorizedException("Geçersiz refresh token.");
        }

        var session = await _dbContext.UserSessions
            .Include(userSession => userSession.User)
            .FirstOrDefaultAsync(userSession => userSession.RefreshToken == refreshToken);

        if (session?.User is null)
        {
            throw new UnauthorizedException("Geçersiz refresh token.");
        }

        if (session.ExpiresAt <= DateTime.UtcNow)
        {
            _dbContext.UserSessions.Remove(session);
            await _dbContext.SaveChangesAsync();

            throw new UnauthorizedException("Süresi dolmuş refresh token.");
        }

        await PromoteShopOwnerToSellerAsync(session.User);

        var tokens = _jwtProvider.GenerateTokens(session.User);

        session.RefreshToken = tokens.RefreshToken;
        session.ExpiresAt = DateTime.UtcNow.AddDays(GetRefreshTokenExpireDays());
        await _dbContext.SaveChangesAsync();

        return tokens;
    }

    public async Task<bool> LogoutAsync(string refreshToken, string accessToken)
    {
        var isSessionRemoved = false;

        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            var session = await _dbContext.UserSessions
                .FirstOrDefaultAsync(userSession => userSession.RefreshToken == refreshToken);

            if (session is not null)
            {
                _dbContext.UserSessions.Remove(session);
                await _dbContext.SaveChangesAsync();
                isSessionRemoved = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            await _cache.SetStringAsync(
                GetAccessTokenBlacklistKey(accessToken),
                "1",
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(AccessTokenBlacklistMinutes)
                });
        }

        return isSessionRemoved || !string.IsNullOrWhiteSpace(accessToken);
    }

    public async Task<UserMeResponseDto> GetCurrentUserAsync(Guid userId)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(user => user.Shop)
            .FirstOrDefaultAsync(user => user.Id == userId);

        if (user is null)
        {
            throw new NotFoundException("Kullanıcı bulunamadı.");
        }

        var shop = user.Shop;
        var effectiveRole = user.Role == UserRole.User && shop is not null
            ? UserRole.Seller
            : user.Role;

        return new UserMeResponseDto(
            Id: user.Id,
            FullName: user.FullName ?? string.Empty,
            Email: user.Email,
            Role: effectiveRole.ToString().ToLowerInvariant(),
            HasShop: shop is not null,
            ShopId: shop?.Id,
            ShopSlug: shop?.Slug,
            ShopIsActive: shop?.IsActive);
    }

    private async Task PromoteShopOwnerToSellerAsync(User user)
    {
        if (user.Role != UserRole.User)
        {
            return;
        }

        try
        {
            // Shop var mı ve aktif subscription'ı var mı kontrol et
            var activeShop = await _dbContext.Shops
                .Where(s => s.UserId == user.Id && s.IsActive == true)
                .FirstOrDefaultAsync();

            if (activeShop is not null)
            {
                // Aktif subscription var mı kontrol et
                var hasActiveSubscription = await _dbContext.SellerSubscriptions
                    .AnyAsync(s => s.ShopId == activeShop.Id && s.Status == Models.Enums.SubStatus.Active);

                if (hasActiveSubscription)
                {
                    user.Role = UserRole.Seller;
                }
            }
        }
        catch (Exception exception)
        {
            // Shop promotion'da hata olursa, session'ı bloke etme
            // Sadece log et
            _logger.LogWarning(
                exception,
                "Error promoting shop owner to seller. UserId: {UserId}",
                user.Id);
        }
    }

    private async Task<TokenDto> IssueTokensAsync(User user)
    {
        var tokens = _jwtProvider.GenerateTokens(user);
        var now = DateTime.UtcNow;

        user.LastLoginAt = now;
        _dbContext.UserSessions.Add(new UserSession
        {
            User = user,
            RefreshToken = tokens.RefreshToken,
            ExpiresAt = now.AddDays(GetRefreshTokenExpireDays())
        });

        await _dbContext.SaveChangesAsync();

        return tokens;
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    private static string GetOtpCacheKey(string email)
    {
        return $"otp:{email}";
    }

    private static string GetAccessTokenBlacklistKey(string accessToken)
    {
        return $"blacklist:{accessToken}";
    }

    private static string GenerateOtpCode()
    {
        return RandomNumberGenerator.GetInt32(1000, 10_000).ToString();
    }

    private static string BuildOtpEmailBody(string? fullName, string otpCode)
    {
        var displayName = string.IsNullOrWhiteSpace(fullName)
            ? "Craftora kullanicisi"
            : WebUtility.HtmlEncode(fullName.Trim());

        return $"""
            <h2>Merhaba {displayName},</h2>
            <p>Craftora e-posta dogrulama kodunuz:</p>
            <h1 style="letter-spacing:4px;">{otpCode}</h1>
            <p>Bu kod 5 dakika boyunca gecerlidir.</p>
            """;
    }

    private int GetRefreshTokenExpireDays()
    {
        return _configuration.GetSection("Jwt")
            .GetValue("RefreshTokenExpireDays", DefaultRefreshTokenExpireDays);
    }
}
