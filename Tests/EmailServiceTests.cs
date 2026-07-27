using System.Net;
using System.Text.Json;
using CraftoraApi.Configuration;
using CraftoraApi.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class EmailServiceTests
{
    [Fact]
    public async Task SendEmail_includes_configured_reply_to()
    {
        var handler = new CapturingHandler();
        var service = new EmailService(
            Options.Create(new EmailSettings
            {
                FromEmail = "noreply@craftoramedya.com",
                FromName = "Craftora",
                ReplyTo = "support@craftoramedya.com",
                Resend = new ResendEmailSettings { ApiKey = "test-key" }
            }),
            new TestHttpClientFactory(handler),
            NullLogger<EmailService>.Instance);

        await service.SendEmailAsync(
            "buyer@example.com",
            "Test",
            "<p>Test</p>",
            isHtml: true);

        using var payload = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal(
            "support@craftoramedya.com",
            payload.RootElement.GetProperty("reply_to").GetString());
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public TestHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://api.resend.com/")
            };
        }
    }
}
