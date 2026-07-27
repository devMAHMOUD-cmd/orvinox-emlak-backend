using System.Security.Cryptography;
using System.Text;

namespace CraftoraApi.Infrastructure.Security;

public static class SvixWebhookVerifier
{
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromMinutes(5);

    public static bool Verify(
        string payload,
        string messageId,
        string timestamp,
        string signatureHeader,
        string secret,
        DateTimeOffset? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(payload) ||
            string.IsNullOrWhiteSpace(messageId) ||
            string.IsNullOrWhiteSpace(timestamp) ||
            string.IsNullOrWhiteSpace(signatureHeader) ||
            string.IsNullOrWhiteSpace(secret) ||
            !long.TryParse(timestamp, out var unixTimestamp))
        {
            return false;
        }

        DateTimeOffset timestampValue;
        try
        {
            timestampValue = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var now = utcNow ?? DateTimeOffset.UtcNow;
        if ((now - timestampValue).Duration() > TimestampTolerance)
        {
            return false;
        }

        byte[] key;
        try
        {
            var encodedSecret = secret.StartsWith("whsec_", StringComparison.Ordinal)
                ? secret["whsec_".Length..]
                : secret;
            key = Convert.FromBase64String(encodedSecret);
        }
        catch (FormatException)
        {
            return false;
        }

        var signedPayload = Encoding.UTF8.GetBytes(
            $"{messageId}.{timestamp}.{payload}");
        var expectedSignature = HMACSHA256.HashData(key, signedPayload);

        foreach (var signature in signatureHeader.Split(
                     ' ',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = signature.Split(',', 2);
            if (parts.Length != 2 ||
                !string.Equals(parts[0], "v1", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                var candidate = Convert.FromBase64String(parts[1]);
                if (candidate.Length == expectedSignature.Length &&
                    CryptographicOperations.FixedTimeEquals(candidate, expectedSignature))
                {
                    return true;
                }
            }
            catch (FormatException)
            {
                // Ignore malformed signatures and continue checking other versions.
            }
        }

        return false;
    }
}
