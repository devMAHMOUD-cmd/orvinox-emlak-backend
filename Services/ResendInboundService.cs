using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CraftoraApi.Configuration;
using CraftoraApi.Data;
using CraftoraApi.DTOs.Notification;
using CraftoraApi.Infrastructure.Security;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CraftoraApi.Services;

public sealed partial class ResendInboundService : IResendInboundService
{
    private const int MaximumSubjectLength = 200;
    private const int MaximumMessageLength = 5000;
    private const string SupportAddressDefault = "support@craftoramedya.com";

    private readonly AppDbContext _dbContext;
    private readonly NpgsqlDataSource _dataSource;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly INotificationService _notificationService;
    private readonly ResendInboundSettings _settings;
    private readonly ILogger<ResendInboundService> _logger;

    public ResendInboundService(
        AppDbContext dbContext,
        NpgsqlDataSource dataSource,
        IHttpClientFactory httpClientFactory,
        INotificationService notificationService,
        IOptions<ResendInboundSettings> options,
        ILogger<ResendInboundService> logger)
    {
        _dbContext = dbContext;
        _dataSource = dataSource;
        _httpClientFactory = httpClientFactory;
        _notificationService = notificationService;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<ResendInboundResult> ProcessAsync(
        string svixId,
        ResendReceivedWebhook webhook,
        CancellationToken cancellationToken = default)
    {
        var supportAddress = string.IsNullOrWhiteSpace(_settings.SupportAddress)
            ? SupportAddressDefault
            : _settings.SupportAddress.Trim();
        var supportRecipient = SupportAddressResolver.Resolve(
            webhook.To.Concat(webhook.ReceivedFor),
            supportAddress);
        if (supportRecipient is null)
        {
            return new ResendInboundResult("ignored");
        }

        var claimed = await ClaimEventAsync(
            svixId,
            webhook,
            supportRecipient.Address,
            cancellationToken);
        if (!claimed)
        {
            return new ResendInboundResult("duplicate");
        }

        try
        {
            var senderEmail = NormalizeEmail(webhook.From);
            var user = await _dbContext.Users
                .AsNoTracking()
                .Where(item =>
                    item.Email.ToLower() == senderEmail &&
                    item.IsActive == true &&
                    item.IsEmailVerified == true &&
                    item.DeletedAt == null)
                .Select(item => new { item.Id })
                .FirstOrDefaultAsync(cancellationToken);

            if (user is null)
            {
                await SetEventStatusAsync(
                    webhook.EmailId,
                    "unmatched",
                    "Kayitli ve dogrulanmis kullanici bulunamadi.",
                    null,
                    cancellationToken);
                _logger.LogWarning(
                    "Inbound support email sender did not match an active verified user. EmailId: {EmailId}, Sender: {Sender}",
                    webhook.EmailId,
                    senderEmail);
                return new ResendInboundResult("unmatched");
            }

            var receivedEmail = await RetrieveEmailAsync(
                webhook.EmailId,
                cancellationToken);
            var subject = NormalizeSubject(receivedEmail.Subject ?? webhook.Subject);
            var message = NormalizeMessage(receivedEmail.Text, receivedEmail.Html);
            var ticketId = supportRecipient.TicketId.HasValue
                ? await AppendTicketAsync(
                    webhook.EmailId,
                    user.Id,
                    supportRecipient.TicketId.Value,
                    message,
                    cancellationToken)
                : null;
            ticketId ??= await CreateTicketAsync(
                webhook.EmailId,
                user.Id,
                subject,
                message,
                cancellationToken);
            var resolvedTicketId = ticketId.Value;

            await NotifyAdminsAsync(resolvedTicketId);
            _logger.LogInformation(
                "Inbound support email converted to ticket. EmailId: {EmailId}, TicketId: {TicketId}, UserId: {UserId}",
                webhook.EmailId,
                resolvedTicketId,
                user.Id);
            return new ResendInboundResult("processed", resolvedTicketId);
        }
        catch (Exception exception)
        {
            await SetEventStatusAsync(
                webhook.EmailId,
                "failed",
                Limit(exception.Message, 1000),
                null,
                CancellationToken.None);
            _logger.LogError(
                exception,
                "Inbound support email processing failed. EmailId: {EmailId}, SvixId: {SvixId}",
                webhook.EmailId,
                svixId);
            throw;
        }
    }

    private async Task<ReceivedEmail> RetrieveEmailAsync(
        Guid emailId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new InvalidOperationException("Resend inbound API key is not configured.");
        }

        var client = _httpClientFactory.CreateClient("ResendInbound");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"emails/receiving/{emailId:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _settings.ApiKey);
        using var response = await client.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Resend Receiving API returned HTTP {(int)response.StatusCode}.");
        }

        return JsonSerializer.Deserialize<ReceivedEmail>(
                   json,
                   new JsonSerializerOptions(JsonSerializerDefaults.Web))
               ?? throw new InvalidOperationException(
                   "Resend Receiving API returned an empty response.");
    }

    private async Task<bool> ClaimEventAsync(
        string svixId,
        ResendReceivedWebhook webhook,
        string recipient,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT public.claim_resend_inbound_event(
                @svix_id,
                @email_id,
                @sender_email,
                @recipient_email,
                @subject
            )
            """,
            connection);
        command.Parameters.AddWithValue("svix_id", svixId);
        command.Parameters.AddWithValue("email_id", webhook.EmailId);
        command.Parameters.AddWithValue("sender_email", NormalizeEmail(webhook.From));
        command.Parameters.AddWithValue("recipient_email", recipient);
        command.Parameters.AddWithValue(
            "subject",
            (object?)Limit(webhook.Subject?.Trim(), MaximumSubjectLength) ?? DBNull.Value);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private async Task<Guid> CreateTicketAsync(
        Guid emailId,
        Guid userId,
        string subject,
        string message,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT public.complete_resend_inbound_support(@email_id, @user_id, @subject, @message)",
            connection);
        command.Parameters.AddWithValue("email_id", emailId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("message", message);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Inbound support ticket could not be created."));
    }

    private async Task<Guid?> AppendTicketAsync(
        Guid emailId,
        Guid userId,
        Guid ticketId,
        string message,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT public.append_resend_inbound_support(@email_id, @user_id, @ticket_id, @message)",
            connection);
        command.Parameters.AddWithValue("email_id", emailId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("ticket_id", ticketId);
        command.Parameters.AddWithValue("message", message);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : (Guid)result;
    }

    private async Task SetEventStatusAsync(
        Guid emailId,
        string status,
        string? errorMessage,
        Guid? ticketId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT public.set_resend_inbound_event_status(@email_id, @status, @error_message, @ticket_id)",
            connection);
        command.Parameters.AddWithValue("email_id", emailId);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue(
            "error_message",
            errorMessage is null ? DBNull.Value : errorMessage);
        command.Parameters.AddWithValue(
            "ticket_id",
            ticketId.HasValue ? ticketId.Value : DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task NotifyAdminsAsync(Guid ticketId)
    {
        var adminIds = await _dbContext.Users
            .AsNoTracking()
            .Where(item =>
                item.Role == UserRole.Admin &&
                item.IsActive == true &&
                item.DeletedAt == null)
            .Select(item => item.Id)
            .ToListAsync();

        foreach (var adminId in adminIds)
        {
            try
            {
                await _notificationService.SendNotificationAsync(
                    adminId,
                    "E-posta ile yeni destek talebi",
                    "Support adresine gelen e-posta destek talebine donusturuldu.",
                    NotificationType.System,
                    ticketId,
                    "support_ticket");
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Inbound support ticket admin notification failed. TicketId: {TicketId}, AdminId: {AdminId}",
                    ticketId,
                    adminId);
            }
        }
    }

    private static string NormalizeEmail(string value)
    {
        var trimmed = value.Trim();
        var start = trimmed.LastIndexOf('<');
        var end = trimmed.LastIndexOf('>');
        var address = start >= 0 && end > start
            ? trimmed[(start + 1)..end]
            : trimmed;
        return address.Trim().ToLowerInvariant();
    }

    private static string NormalizeSubject(string? value)
    {
        var subject = string.IsNullOrWhiteSpace(value)
            ? "E-posta destek talebi"
            : CollapseWhitespaceRegex().Replace(value.Trim(), " ");
        return Limit(subject, MaximumSubjectLength) ?? "E-posta destek talebi";
    }

    private static string NormalizeMessage(string? text, string? html)
    {
        var value = !string.IsNullOrWhiteSpace(text)
            ? text
            : HtmlToText(html);
        value = ProhibitedControlCharactersRegex().Replace(value ?? string.Empty, string.Empty);
        value = EmailReplyTextNormalizer.TrimQuotedHistory(value);
        value = ExcessiveBlankLinesRegex().Replace(value.Trim(), "\n\n");
        if (string.IsNullOrWhiteSpace(value))
        {
            value = "E-posta govdesi bos.";
        }

        return Limit(value, MaximumMessageLength) ?? "E-posta govdesi bos.";
    }

    private static string HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withoutUnsafeBlocks = HtmlUnsafeBlockRegex().Replace(html, string.Empty);
        var withLines = HtmlLineBreakRegex().Replace(withoutUnsafeBlocks, "\n");
        var withoutTags = HtmlTagRegex().Replace(withLines, string.Empty);
        return WebUtility.HtmlDecode(withoutTags);
    }

    private static string? Limit(string? value, int maximumLength)
    {
        return value is null || value.Length <= maximumLength
            ? value
            : value[..maximumLength];
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex CollapseWhitespaceRegex();

    [GeneratedRegex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]", RegexOptions.CultureInvariant)]
    private static partial Regex ProhibitedControlCharactersRegex();

    [GeneratedRegex(@"\n{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex ExcessiveBlankLinesRegex();

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlUnsafeBlockRegex();

    [GeneratedRegex(@"<(br|/p|/div|/li|/tr|/h[1-6])\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlLineBreakRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    private sealed record ReceivedEmail(
        string Id,
        string From,
        IReadOnlyList<string> To,
        string? Subject,
        string? Html,
        string? Text,
        [property: JsonPropertyName("message_id")] string? MessageId);

}
