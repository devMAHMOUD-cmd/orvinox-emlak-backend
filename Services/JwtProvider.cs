using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CraftoraApi.DTOs.Auth;
using CraftoraApi.Models.Entities;
using CraftoraApi.Services.Interfaces;
using Microsoft.IdentityModel.Tokens;

namespace CraftoraApi.Services;

public sealed class JwtProvider : IJwtProvider
{
    private const int RefreshTokenByteSize = 32;

    private readonly IConfiguration _configuration;

    public JwtProvider(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public TokenDto GenerateTokens(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var jwtSettings = _configuration.GetSection("Jwt");
        var secret = jwtSettings["Secret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("JWT Secret not found in appsettings.");
        }

        var issuer = jwtSettings["Issuer"] ?? "CraftoraApi";
        var audience = jwtSettings["Audience"] ?? "CraftoraApp";
        var accessTokenExpireMinutes = jwtSettings.GetValue("AccessTokenExpireMinutes", 15);
        _ = jwtSettings.GetValue("RefreshTokenExpireDays", 30);

        var expiresAt = DateTime.UtcNow.AddMinutes(accessTokenExpireMinutes);
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256Signature);
        var role = user.Role.ToString().ToLowerInvariant();

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, role),
            new Claim("role", role)
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        var refreshToken = GenerateRefreshToken();

        return new TokenDto(
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            ExpiresIn: accessTokenExpireMinutes * 60);
    }

    private static string GenerateRefreshToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(RefreshTokenByteSize);
        return Convert.ToBase64String(randomBytes);
    }
}
