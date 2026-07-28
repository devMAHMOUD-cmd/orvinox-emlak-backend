using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.WebUtilities;

namespace CraftoraApi.Services.Discovery;

public sealed class DiscoveryFeedCursorService : IDiscoveryFeedCursorService
{
    private const int CursorVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly byte[] _signingKey;

    public DiscoveryFeedCursorService(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var applicationSecret = Environment.GetEnvironmentVariable(
            "DISCOVERY_TRACKING_SECRET");
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
                "Discovery cursor signing secret must contain at least 32 characters.");
        }

        _signingKey = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"craftora.discovery.cursor.v1:{applicationSecret}"));
    }

    public string Issue(
        Guid userId,
        Guid feedSessionId,
        int offset,
        DateTimeOffset expiresAt)
    {
        if (userId == Guid.Empty || feedSessionId == Guid.Empty || offset < 0)
        {
            throw new ArgumentException("Discovery cursor context is invalid.");
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new CursorPayload(
                CursorVersion,
                userId,
                feedSessionId,
                offset,
                expiresAt.ToUnixTimeSeconds()),
            SerializerOptions);
        var signature = HMACSHA256.HashData(_signingKey, payload);

        return $"{WebEncoders.Base64UrlEncode(payload)}.{WebEncoders.Base64UrlEncode(signature)}";
    }

    public bool TryRead(
        string token,
        Guid expectedUserId,
        out DiscoveryFeedCursorContext context)
    {
        context = default!;
        if (expectedUserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(token) ||
            token.Length > 4096)
        {
            return false;
        }

        var parts = token.Split('.', StringSplitOptions.None);
        if (parts.Length != 2)
        {
            return false;
        }

        try
        {
            var payloadBytes = WebEncoders.Base64UrlDecode(parts[0]);
            var suppliedSignature = WebEncoders.Base64UrlDecode(parts[1]);
            var expectedSignature = HMACSHA256.HashData(_signingKey, payloadBytes);
            if (!CryptographicOperations.FixedTimeEquals(
                    suppliedSignature,
                    expectedSignature))
            {
                return false;
            }

            var payload = JsonSerializer.Deserialize<CursorPayload>(
                payloadBytes,
                SerializerOptions);
            if (payload is null ||
                payload.Version != CursorVersion ||
                payload.UserId != expectedUserId ||
                payload.FeedSessionId == Guid.Empty ||
                payload.Offset < 0)
            {
                return false;
            }

            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAtUnix);
            if (expiresAt <= DateTimeOffset.UtcNow)
            {
                return false;
            }

            context = new DiscoveryFeedCursorContext(
                payload.UserId,
                payload.FeedSessionId,
                payload.Offset,
                expiresAt);
            return true;
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private sealed record CursorPayload(
        int Version,
        Guid UserId,
        Guid FeedSessionId,
        int Offset,
        long ExpiresAtUnix);
}
