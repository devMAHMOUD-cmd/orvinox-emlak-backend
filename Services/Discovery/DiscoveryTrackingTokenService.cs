using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CraftoraApi.Services.Interfaces;

namespace CraftoraApi.Services.Discovery;

public sealed class DiscoveryTrackingTokenService : IDiscoveryTrackingTokenService
{
    public const string CurrentAlgorithmVersion = "organic-v1";
    private const int TokenVersion = 1;
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(2);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly byte[] _signingKey;

    public DiscoveryTrackingTokenService(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var applicationSecret = Environment.GetEnvironmentVariable("DISCOVERY_TRACKING_SECRET");
        if (string.IsNullOrWhiteSpace(applicationSecret))
        {
            applicationSecret = configuration["Discovery:TrackingSecret"];
        }

        if (string.IsNullOrWhiteSpace(applicationSecret))
        {
            applicationSecret = configuration["Jwt:Secret"]
                ?? Environment.GetEnvironmentVariable("JWT_SECRET");
        }

        if (string.IsNullOrWhiteSpace(applicationSecret) || applicationSecret.Length < 32)
        {
            throw new InvalidOperationException(
                "Discovery tracking token signing secret must contain at least 32 characters.");
        }

        _signingKey = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"craftora.discovery.tracking.v1:{applicationSecret}"));
    }

    public string Issue(
        Guid? userId,
        string contentType,
        Guid contentId,
        Guid shopId,
        Guid feedSessionId,
        int position,
        bool isSponsored = false,
        Guid? boostId = null)
    {
        if (contentId == Guid.Empty || shopId == Guid.Empty || feedSessionId == Guid.Empty)
        {
            throw new ArgumentException("Discovery tracking identifiers cannot be empty.");
        }

        if (position < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        if (isSponsored != boostId.HasValue || boostId == Guid.Empty)
        {
            throw new ArgumentException("Discovery sponsorship context is invalid.");
        }

        var normalizedContentType = NormalizeContentType(contentType);
        if (string.IsNullOrEmpty(normalizedContentType))
        {
            throw new ArgumentException(
                "Unsupported discovery content type.",
                nameof(contentType));
        }

        var now = DateTimeOffset.UtcNow;
        var payload = new TrackingTokenPayload(
            Version: TokenVersion,
            TokenId: Guid.NewGuid(),
            UserId: userId,
            ContentType: normalizedContentType,
            ContentId: contentId,
            ShopId: shopId,
            FeedSessionId: feedSessionId,
            Position: position,
            AlgorithmVersion: CurrentAlgorithmVersion,
            IssuedAtUnixSeconds: now.ToUnixTimeSeconds(),
            ExpiresAtUnixSeconds: now.Add(TokenLifetime).ToUnixTimeSeconds(),
            IsSponsored: isSponsored,
            BoostId: boostId);
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);
        var signature = HMACSHA256.HashData(_signingKey, payloadBytes);

        return $"{Base64UrlEncode(payloadBytes)}.{Base64UrlEncode(signature)}";
    }

    public bool TryValidate(
        string token,
        Guid currentUserId,
        out DiscoveryTrackingContext context)
    {
        context = default!;
        if (string.IsNullOrWhiteSpace(token) || currentUserId == Guid.Empty)
        {
            return false;
        }

        var parts = token.Split('.', 2);
        if (parts.Length != 2 ||
            !TryBase64UrlDecode(parts[0], out var payloadBytes) ||
            !TryBase64UrlDecode(parts[1], out var providedSignature))
        {
            return false;
        }

        var expectedSignature = HMACSHA256.HashData(_signingKey, payloadBytes);
        if (providedSignature.Length != expectedSignature.Length ||
            !CryptographicOperations.FixedTimeEquals(providedSignature, expectedSignature))
        {
            return false;
        }

        TrackingTokenPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TrackingTokenPayload>(
                payloadBytes,
                SerializerOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (payload is null ||
            payload.Version != TokenVersion ||
            payload.TokenId == Guid.Empty ||
            payload.ContentId == Guid.Empty ||
            payload.ShopId == Guid.Empty ||
            payload.FeedSessionId == Guid.Empty ||
            payload.Position < 0 ||
            payload.IsSponsored != payload.BoostId.HasValue ||
            payload.BoostId == Guid.Empty ||
            payload.UserId.HasValue && payload.UserId.Value != currentUserId ||
            !string.Equals(
                payload.ContentType,
                NormalizeContentType(payload.ContentType),
                StringComparison.Ordinal) ||
            !string.Equals(
                payload.AlgorithmVersion,
                CurrentAlgorithmVersion,
                StringComparison.Ordinal))
        {
            return false;
        }

        DateTimeOffset issuedAt;
        DateTimeOffset expiresAt;
        try
        {
            issuedAt = DateTimeOffset.FromUnixTimeSeconds(payload.IssuedAtUnixSeconds);
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAtUnixSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (issuedAt > now.AddMinutes(1) ||
            expiresAt <= now ||
            expiresAt <= issuedAt ||
            expiresAt - issuedAt > TokenLifetime.Add(TimeSpan.FromMinutes(1)))
        {
            return false;
        }

        context = new DiscoveryTrackingContext(
            payload.TokenId,
            payload.UserId,
            payload.ContentType,
            payload.ContentId,
            payload.ShopId,
            payload.FeedSessionId,
            payload.Position,
            payload.AlgorithmVersion,
            issuedAt,
            expiresAt,
            payload.IsSponsored,
            payload.BoostId);
        return true;
    }

    private static string NormalizeContentType(string contentType)
    {
        return contentType?.Trim().ToLowerInvariant() switch
        {
            "media" => "media",
            "product" => "product",
            "course" => "course",
            "shop" => "shop",
            _ => string.Empty
        };
    }

    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool TryBase64UrlDecode(string value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            0 => string.Empty,
            _ => "invalid"
        };

        try
        {
            bytes = Convert.FromBase64String(padded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed record TrackingTokenPayload(
        int Version,
        Guid TokenId,
        Guid? UserId,
        string ContentType,
        Guid ContentId,
        Guid ShopId,
        Guid FeedSessionId,
        int Position,
        string AlgorithmVersion,
        long IssuedAtUnixSeconds,
        long ExpiresAtUnixSeconds,
        bool IsSponsored,
        Guid? BoostId);
}
