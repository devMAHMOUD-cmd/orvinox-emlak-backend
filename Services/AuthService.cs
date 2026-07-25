using System.Security.Cryptography;
using System.Data;
using System.Data.Common;
using System.Net;
using System.Text;
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
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Distributed;

namespace CraftoraApi.Services;

public sealed class AuthService : IAuthService
{
    private const int OtpExpirationMinutes = 5;
    private const int DefaultRefreshTokenExpireDays = 30;
    private const int MaxFailedLoginAttempts = 5;
    private const int BruteForceWindowMinutes = 30;
    private const int AccessTokenBlacklistMinutes = 15;
    private const int MaxActiveSessionsPerUser = 5;
    private const int MaxDeviceIdLength = 255;

    private readonly AppDbContext _dbContext;
    private readonly IDistributedCache _cache;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IJwtProvider _jwtProvider;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AppDbContext dbContext,
        IDistributedCache cache,
        IPublishEndpoint publishEndpoint,
        IJwtProvider jwtProvider,
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuthService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _publishEndpoint = publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));
        _jwtProvider = jwtProvider ?? throw new ArgumentNullException(nameof(jwtProvider));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
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
            Subject: "Craftora e-posta doğrulama kodu",
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

        var connection = _dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await _dbContext.Database.OpenConnectionAsync();
        }

        try
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.current_user_id', {user.Id.ToString("D")}, true);");

            var updatedRows = await _dbContext.Users
                .Where(candidate => candidate.Id == user.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.IsEmailVerified, true));

            if (updatedRows != 1)
            {
                await transaction.RollbackAsync();
                throw new InvalidOperationException("E-posta doğrulama durumu güncellenemedi.");
            }

            await transaction.CommitAsync();
        }
        finally
        {
            if (openedHere)
            {
                await _dbContext.Database.CloseConnectionAsync();
            }
        }

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

        await EnsureUserCanAuthenticateAsync(user);
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
        var configuredClientIds = _configuration["OAuth:Google:ClientIds"];
        var clientIds = (string.IsNullOrWhiteSpace(configuredClientIds)
                ? clientId
                : configuredClientIds)
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();

        if (clientIds.Length == 0)
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
                    Audience = clientIds
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

        await EnsureUserCanAuthenticateAsync(user);
        await PromoteShopOwnerToSellerAsync(user);

        return await IssueTokensAsync(user);
    }

    public async Task<TokenDto> RefreshTokenAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new UnauthorizedException("Geçersiz refresh token.");
        }

        var metadata = GetSessionMetadata();
        var nextRefreshToken = _jwtProvider.GenerateRefreshToken();
        var nextRefreshTokenHash = HashRefreshToken(nextRefreshToken);
        var nextExpiresAt = DateTime.UtcNow.AddDays(GetRefreshTokenExpireDays());

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        var userId = await RotateSessionAsync(
            HashRefreshToken(refreshToken),
            nextRefreshTokenHash,
            nextExpiresAt,
            metadata,
            transaction.GetDbTransaction());

        if (!userId.HasValue)
        {
            await transaction.RollbackAsync();
            throw new UnauthorizedException("Geçersiz veya süresi dolmuş refresh token.");
        }

        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.current_user_id', {userId.Value.ToString("D")}, true);");

        var user = await _dbContext.Users.FirstOrDefaultAsync(candidate => candidate.Id == userId.Value);
        if (user is null)
        {
            await transaction.RollbackAsync();
            throw new UnauthorizedException("Geçersiz refresh token.");
        }

        await EnsureUserCanAuthenticateAsync(user);
        await PromoteShopOwnerToSellerAsync(user);

        var tokens = _jwtProvider.GenerateTokens(user, nextRefreshToken);
        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        return tokens;
    }

    public async Task<bool> LogoutAsync(string refreshToken, string accessToken)
    {
        var isSessionRevoked = false;

        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            isSessionRevoked = await RevokeSessionAsync(HashRefreshToken(refreshToken));
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

        return isSessionRevoked || !string.IsNullOrWhiteSpace(accessToken);
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
        var effectiveRole = user.Role == UserRole.Admin
            ? UserRole.Admin
            : await HasActiveSellerAccessAsync(user.Id)
                ? UserRole.Seller
                : UserRole.User;

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

    private async Task<bool> HasActiveSellerAccessAsync(Guid userId)
    {
        var now = DateTime.UtcNow;

        return await _dbContext.Shops
            .AsNoTracking()
            .AnyAsync(shop =>
                shop.UserId == userId &&
                shop.IsActive == true &&
                _dbContext.SellerSubscriptions.Any(subscription =>
                    subscription.ShopId == shop.Id &&
                    subscription.Status == SubStatus.Active &&
                    subscription.CurrentPeriodEnd > now));
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
        await EnsureUserCanAuthenticateAsync(user);

        var tokens = _jwtProvider.GenerateTokens(user);
        var now = DateTime.UtcNow;
        var metadata = GetSessionMetadata();
        var isFirstLogin = false;

        // Login is still anonymous at middleware level. Set the RLS identity
        // before creating the session and updating the user's last-login time.
        var connection = _dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await _dbContext.Database.OpenConnectionAsync();
        }

        try
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.current_user_id', {user.Id.ToString("D")}, true);");
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({user.Id.ToString("D")}, 0));");

            var databaseValues = await _dbContext.Entry(user).GetDatabaseValuesAsync();
            isFirstLogin = databaseValues?.GetValue<DateTime?>(nameof(User.LastLoginAt)) is null;

            await _dbContext.UserSessions
                .Where(session =>
                    session.UserId == user.Id &&
                    session.IsRevoked != true &&
                    session.ExpiresAt <= now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(session => session.IsRevoked, true));

            if (!string.IsNullOrWhiteSpace(metadata.DeviceId))
            {
                await _dbContext.UserSessions
                    .Where(session =>
                        session.UserId == user.Id &&
                        session.DeviceId == metadata.DeviceId &&
                        session.IsRevoked != true &&
                        session.ExpiresAt > now)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(session => session.IsRevoked, true));
            }

            var sessionIdsToRevoke = await _dbContext.UserSessions
                .Where(session =>
                    session.UserId == user.Id &&
                    session.IsRevoked != true &&
                    session.ExpiresAt > now)
                .OrderByDescending(session => session.CreatedAt.HasValue)
                .ThenByDescending(session => session.CreatedAt)
                .ThenByDescending(session => session.Id)
                .Skip(MaxActiveSessionsPerUser - 1)
                .Select(session => session.Id)
                .ToListAsync();

            if (sessionIdsToRevoke.Count > 0)
            {
                await _dbContext.UserSessions
                    .Where(session => sessionIdsToRevoke.Contains(session.Id))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(session => session.IsRevoked, true));
            }

            user.LastLoginAt = now;
            _dbContext.UserSessions.Add(new UserSession
            {
                User = user,
                RefreshToken = HashRefreshToken(tokens.RefreshToken),
                ExpiresAt = now.AddDays(GetRefreshTokenExpireDays()),
                DeviceId = metadata.DeviceId,
                IpAddress = metadata.IpAddress,
                UserAgent = metadata.UserAgent,
                IsRevoked = false
            });

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        finally
        {
            if (connection.State == ConnectionState.Open)
            {
                await _dbContext.Database.ExecuteSqlRawAsync("RESET app.current_user_id;");
            }

            if (openedHere)
            {
                await _dbContext.Database.CloseConnectionAsync();
            }
        }

        if (isFirstLogin)
        {
            try
            {
                await _publishEndpoint.Publish(new SendEmailCommand(
                    To: user.Email,
                    Subject: "Craftora'ya hoş geldin",
                    Body: BuildWelcomeEmailBody(user.FullName),
                    IsHtml: true));

                _logger.LogInformation(
                    "Welcome email command published. UserId: {UserId}, Email: {Email}",
                    user.Id,
                    user.Email);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Welcome email command could not be published. UserId: {UserId}, Email: {Email}",
                    user.Id,
                    user.Email);
            }
        }

        return tokens;
    }

    private static Task EnsureUserCanAuthenticateAsync(User user)
    {
        if (user.DeletedAt is not null)
        {
            throw new UnauthorizedException("Hesap kullanima kapatildi.");
        }

        if (user.IsActive != true)
        {
            throw new UnauthorizedException("Hesap askiya alindi.");
        }

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
        {
            throw new AccountLockedException(user.LockReason, user.LockedUntil.Value);
        }

        return Task.CompletedTask;
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

    private static string HashRefreshToken(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
    }

    private SessionMetadata GetSessionMetadata()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context is null)
        {
            return new SessionMetadata(null, null, null);
        }

        var deviceId = context.Request.Headers["X-Device-Id"].ToString().Trim();
        if (deviceId.Length > MaxDeviceIdLength)
        {
            deviceId = deviceId[..MaxDeviceIdLength];
        }

        var userAgent = context.Request.Headers.UserAgent.ToString().Trim();

        return new SessionMetadata(
            string.IsNullOrWhiteSpace(deviceId) ? null : deviceId,
            context.Connection.RemoteIpAddress,
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent);
    }

    private async Task<Guid?> RotateSessionAsync(
        string currentRefreshTokenHash,
        string nextRefreshTokenHash,
        DateTime nextExpiresAt,
        SessionMetadata metadata,
        DbTransaction transaction)
    {
        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT public.rotate_user_session(
                CAST(@current_hash AS text),
                CAST(@next_hash AS text),
                CAST(@next_expires_at AS timestamp with time zone),
                CAST(@device_id AS text),
                CAST(@ip_address AS inet),
                CAST(@user_agent AS text))
            """;

        AddParameter(command, "current_hash", currentRefreshTokenHash);
        AddParameter(command, "next_hash", nextRefreshTokenHash);
        AddParameter(command, "next_expires_at", nextExpiresAt);
        AddParameter(command, "device_id", metadata.DeviceId);
        AddParameter(command, "ip_address", metadata.IpAddress);
        AddParameter(command, "user_agent", metadata.UserAgent);

        var result = await command.ExecuteScalarAsync();
        return result is Guid userId ? userId : null;
    }

    private async Task<bool> RevokeSessionAsync(string refreshTokenHash)
    {
        var connection = _dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await _dbContext.Database.OpenConnectionAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT public.revoke_user_session(@refresh_token_hash);";
            AddParameter(command, "refresh_token_hash", refreshTokenHash);

            var result = await command.ExecuteScalarAsync();
            return result is true;
        }
        finally
        {
            if (openedHere)
            {
                await _dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string GenerateOtpCode()
    {
        return RandomNumberGenerator.GetInt32(1000, 10_000).ToString();
    }

    private static string BuildOtpEmailBody(string? fullName, string otpCode)
    {
        var displayName = string.IsNullOrWhiteSpace(fullName)
            ? "Craftora kullanıcısı"
            : WebUtility.HtmlEncode(fullName.Trim());

        return $"""
            <!doctype html>
            <html lang="tr">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Craftora e-posta doğrulama</title>
            </head>
            <body style="margin:0;padding:0;background:#f4f6f8;color:#17202a;font-family:Arial,Helvetica,sans-serif;">
              <div style="display:none;max-height:0;overflow:hidden;opacity:0;">
                Craftora doğrulama kodunuz: {otpCode}
              </div>
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="background:#f4f6f8;">
                <tr>
                  <td align="center" style="padding:32px 16px;">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0"
                           style="max-width:560px;background:#ffffff;border:1px solid #e3e8ee;border-radius:8px;">
                      <tr>
                        <td style="padding:28px 32px 20px;border-bottom:1px solid #e8edf2;">
                          <span style="display:inline-block;margin-right:10px;padding:8px 11px;background:#0c6b78;border-radius:6px;color:#ffffff;font-size:18px;font-weight:700;line-height:1;">
                            C
                          </span>
                          <span style="display:inline-block;color:#0c6b78;font-size:24px;font-weight:700;line-height:1;vertical-align:middle;">
                            CRAFTORA
                          </span>
                          <div style="margin-top:7px;color:#66737f;font-size:13px;line-height:20px;">
                            Güvenli hesap doğrulama
                          </div>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:28px 32px 32px;">
                          <h1 style="margin:0 0 16px;font-size:22px;line-height:30px;color:#17202a;">
                            Merhaba {displayName},
                          </h1>
                          <p style="margin:0 0 22px;color:#425466;font-size:16px;line-height:25px;">
                            Craftora hesabını doğrulamak için aşağıdaki kodu kullan:
                          </p>
                          <div style="margin:0 0 22px;padding:18px 20px;background:#eef7f8;border:1px solid #b9dfe3;border-radius:6px;text-align:center;">
                            <span style="color:#084f59;font-size:34px;font-weight:700;letter-spacing:8px;line-height:42px;">
                              {otpCode}
                            </span>
                          </div>
                          <p style="margin:0 0 18px;color:#425466;font-size:14px;line-height:22px;">
                            Bu kod <strong>5 dakika</strong> boyunca geçerlidir.
                          </p>
                          <p style="margin:0;color:#697985;font-size:13px;line-height:20px;">
                            Bu işlemi sen başlatmadıysan bu e-postayı yok sayabilirsin. Kodunu hiç kimseyle paylaşma.
                          </p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:20px 32px;background:#f8fafb;border-top:1px solid #e8edf2;color:#7a8792;font-size:12px;line-height:18px;">
                          Bu mesaj Craftora hesabınla ilgili otomatik olarak gönderildi.<br>
                          &copy; {DateTime.UtcNow.Year} Craftora
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }

    private static string BuildWelcomeEmailBody(string? fullName)
    {
        var displayName = string.IsNullOrWhiteSpace(fullName)
            ? "Craftora kullanıcısı"
            : WebUtility.HtmlEncode(fullName.Trim());

        return $"""
            <!doctype html>
            <html lang="tr">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Craftora'ya hoş geldin</title>
            </head>
            <body style="margin:0;padding:0;background:#f4f6f8;color:#17202a;font-family:Arial,Helvetica,sans-serif;">
              <div style="display:none;max-height:0;overflow:hidden;opacity:0;">
                Üret, keşfet ve dijital vitrininle büyü.
              </div>
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="background:#f4f6f8;">
                <tr>
                  <td align="center" style="padding:32px 16px;">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0"
                           style="max-width:560px;background:#ffffff;border:1px solid #e3e8ee;border-radius:8px;">
                      <tr>
                        <td style="padding:28px 32px 20px;border-bottom:1px solid #e8edf2;">
                          <span style="display:inline-block;margin-right:10px;padding:8px 11px;background:#0c6b78;border-radius:6px;color:#ffffff;font-size:18px;font-weight:700;line-height:1;">
                            C
                          </span>
                          <span style="display:inline-block;color:#0c6b78;font-size:24px;font-weight:700;line-height:1;vertical-align:middle;">
                            CRAFTORA
                          </span>
                          <div style="margin-top:7px;color:#66737f;font-size:13px;line-height:20px;">
                            Üret, keşfet ve büyü
                          </div>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:28px 32px 14px;">
                          <h1 style="margin:0 0 14px;font-size:24px;line-height:32px;color:#17202a;">
                            Hoş geldin, {displayName}
                          </h1>
                          <p style="margin:0;color:#425466;font-size:16px;line-height:25px;">
                            Craftora; üreticiler, satıcılar ve yeni fikirler keşfetmek isteyenler için oluşturulmuş bir dijital üretim ve alışveriş platformudur.
                          </p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:10px 32px 28px;">
                          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0">
                            <tr>
                              <td style="padding:14px 0;border-bottom:1px solid #e8edf2;">
                                <strong style="display:block;margin-bottom:4px;color:#17202a;font-size:15px;">Keşfet</strong>
                                <span style="color:#5d6b78;font-size:14px;line-height:21px;">Ürünleri, mağazaları, kursları ve kısa videoları tek akışta incele.</span>
                              </td>
                            </tr>
                            <tr>
                              <td style="padding:14px 0;border-bottom:1px solid #e8edf2;">
                                <strong style="display:block;margin-bottom:4px;color:#17202a;font-size:15px;">Kaydet ve takip et</strong>
                                <span style="color:#5d6b78;font-size:14px;line-height:21px;">Beğendiğin içerikleri kaydet, üreticileri takip et ve yenilikleri kaçırma.</span>
                              </td>
                            </tr>
                            <tr>
                              <td style="padding:14px 0;">
                                <strong style="display:block;margin-bottom:4px;color:#17202a;font-size:15px;">Kendi vitrinini oluştur</strong>
                                <span style="color:#5d6b78;font-size:14px;line-height:21px;">Mağazanı açarak ürünlerini, eğitimlerini ve içeriklerini topluluğa ulaştır.</span>
                              </td>
                            </tr>
                          </table>
                          <p style="margin:18px 0 0;color:#425466;font-size:14px;line-height:22px;">
                            Hazırsan Craftora uygulamasını aç ve keşfetmeye başla.
                          </p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:20px 32px;background:#f8fafb;border-top:1px solid #e8edf2;color:#7a8792;font-size:12px;line-height:18px;">
                          Bu mesaj Craftora hesabındaki ilk başarılı girişin ardından gönderildi.<br>
                          &copy; {DateTime.UtcNow.Year} Craftora
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }

    private int GetRefreshTokenExpireDays()
    {
        return _configuration.GetSection("Jwt")
            .GetValue("RefreshTokenExpireDays", DefaultRefreshTokenExpireDays);
    }

    private sealed record SessionMetadata(
        string? DeviceId,
        IPAddress? IpAddress,
        string? UserAgent);
}
