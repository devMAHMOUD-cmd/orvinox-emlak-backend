using System.Security.Cryptography;
using System.Text;
using CraftoraApi.Infrastructure.Security;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class SvixWebhookVerifierTests
{
    [Fact]
    public void Valid_signature_is_accepted()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1785179000);
        var secretBytes = Encoding.UTF8.GetBytes("craftora-test-webhook-secret");
        var secret = $"whsec_{Convert.ToBase64String(secretBytes)}";
        var payload = """{"type":"email.received"}""";
        var messageId = "msg_craftora_test";
        var timestamp = now.ToUnixTimeSeconds().ToString();
        var signedPayload = Encoding.UTF8.GetBytes(
            $"{messageId}.{timestamp}.{payload}");
        var signature = Convert.ToBase64String(
            HMACSHA256.HashData(secretBytes, signedPayload));

        var result = SvixWebhookVerifier.Verify(
            payload,
            messageId,
            timestamp,
            $"v1,{signature}",
            secret,
            now);

        Assert.True(result);
    }

    [Fact]
    public void Modified_payload_is_rejected()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1785179000);
        var secretBytes = Encoding.UTF8.GetBytes("craftora-test-webhook-secret");
        var secret = $"whsec_{Convert.ToBase64String(secretBytes)}";
        var timestamp = now.ToUnixTimeSeconds().ToString();
        var signature = Convert.ToBase64String(
            HMACSHA256.HashData(
                secretBytes,
                Encoding.UTF8.GetBytes(
                    $"msg_test.{timestamp}.{{\"type\":\"email.received\"}}")));

        Assert.False(SvixWebhookVerifier.Verify(
            """{"type":"email.sent"}""",
            "msg_test",
            timestamp,
            $"v1,{signature}",
            secret,
            now));
    }

    [Fact]
    public void Stale_timestamp_is_rejected()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1785179000);
        var secret = $"whsec_{Convert.ToBase64String(Encoding.UTF8.GetBytes("secret"))}";

        Assert.False(SvixWebhookVerifier.Verify(
            "{}",
            "msg_test",
            now.AddMinutes(-6).ToUnixTimeSeconds().ToString(),
            "v1,invalid",
            secret,
            now));
    }

    [Fact]
    public void Out_of_range_timestamp_is_rejected()
    {
        var secret = $"whsec_{Convert.ToBase64String(Encoding.UTF8.GetBytes("secret"))}";

        Assert.False(SvixWebhookVerifier.Verify(
            "{}",
            "msg_test",
            long.MaxValue.ToString(),
            "v1,invalid",
            secret));
    }
}
