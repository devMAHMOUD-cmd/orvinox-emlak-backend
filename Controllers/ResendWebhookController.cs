using System.Text.Json;
using System.Text.Json.Serialization;
using CraftoraApi.Configuration;
using CraftoraApi.Infrastructure.Security;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CraftoraApi.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/webhooks/resend")]
public sealed class ResendWebhookController : ControllerBase
{
    private const int MaximumWebhookBytes = 1024 * 1024;

    private readonly ResendInboundSettings _settings;
    private readonly IResendInboundService _inboundService;
    private readonly ILogger<ResendWebhookController> _logger;

    public ResendWebhookController(
        IOptions<ResendInboundSettings> options,
        IResendInboundService inboundService,
        ILogger<ResendWebhookController> logger)
    {
        _settings = options.Value;
        _inboundService = inboundService;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(MaximumWebhookBytes)]
    public async Task<IActionResult> ReceiveAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.WebhookSecret))
        {
            _logger.LogError("Resend inbound webhook secret is not configured.");
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { message = "Webhook yapilandirmasi hazir degil." });
        }

        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        var svixId = Request.Headers["svix-id"].ToString();
        var svixTimestamp = Request.Headers["svix-timestamp"].ToString();
        var svixSignature = Request.Headers["svix-signature"].ToString();

        if (!SvixWebhookVerifier.Verify(
                payload,
                svixId,
                svixTimestamp,
                svixSignature,
                _settings.WebhookSecret))
        {
            return BadRequest(new { message = "Gecersiz webhook imzasi." });
        }

        ResendWebhookEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ResendWebhookEnvelope>(
                payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return BadRequest(new { message = "Gecersiz webhook govdesi." });
        }

        if (envelope?.Type != "email.received")
        {
            return Ok(new { status = "ignored" });
        }

        if (envelope.Data is null ||
            !Guid.TryParse(envelope.Data.EmailId, out var emailId) ||
            string.IsNullOrWhiteSpace(envelope.Data.From))
        {
            return BadRequest(new { message = "Eksik email.received verisi." });
        }

        var result = await _inboundService.ProcessAsync(
            svixId,
            new ResendReceivedWebhook(
                envelope.Type,
                emailId,
                envelope.Data.From,
                envelope.Data.To ?? Array.Empty<string>(),
                envelope.Data.ReceivedFor ?? Array.Empty<string>(),
                envelope.Data.Subject),
            cancellationToken);

        return Ok(result);
    }

    private sealed record ResendWebhookEnvelope(
        string Type,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        ResendWebhookData? Data);

    private sealed record ResendWebhookData(
        [property: JsonPropertyName("email_id")] string EmailId,
        string From,
        IReadOnlyList<string>? To,
        [property: JsonPropertyName("received_for")] IReadOnlyList<string>? ReceivedFor,
        string? Subject);
}
