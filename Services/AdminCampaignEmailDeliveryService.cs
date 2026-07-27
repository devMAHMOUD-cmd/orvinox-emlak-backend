using System.Data;
using CraftoraApi.Infrastructure.Services;
using CraftoraApi.Services.Interfaces;
using Npgsql;

namespace CraftoraApi.Services;

public sealed class AdminCampaignEmailDeliveryService : IAdminCampaignEmailDeliveryService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IEmailService _emailService;
    private readonly ILogger<AdminCampaignEmailDeliveryService> _logger;

    public AdminCampaignEmailDeliveryService(
        NpgsqlDataSource dataSource,
        IEmailService emailService,
        ILogger<AdminCampaignEmailDeliveryService> logger)
    {
        _dataSource = dataSource;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task DeliverAsync(
        Guid recipientId,
        CancellationToken cancellationToken = default)
    {
        var delivery = await ClaimAsync(recipientId, cancellationToken);
        if (delivery is null)
        {
            _logger.LogInformation(
                "Admin campaign recipient already processed or unavailable. RecipientId: {RecipientId}",
                recipientId);
            return;
        }

        try
        {
            await _emailService.SendEmailAsync(
                delivery.Email,
                delivery.Subject,
                AdminEmailTemplate.Build(delivery.FullName, delivery.Message),
                true,
                cancellationToken);
            await CompleteAsync(recipientId, true, null, cancellationToken);

            _logger.LogInformation(
                "Admin campaign email sent. CampaignId: {CampaignId}, RecipientId: {RecipientId}, To: {To}",
                delivery.CampaignId,
                recipientId,
                delivery.Email);
        }
        catch (Exception exception)
        {
            await CompleteAsync(
                recipientId,
                false,
                LimitError(exception.Message),
                CancellationToken.None);
            _logger.LogError(
                exception,
                "Admin campaign email failed. CampaignId: {CampaignId}, RecipientId: {RecipientId}, To: {To}",
                delivery.CampaignId,
                recipientId,
                delivery.Email);
            throw;
        }
    }

    private async Task<ClaimedDelivery?> ClaimAsync(
        Guid recipientId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT campaign_id, email, full_name, subject, message
            FROM public.claim_admin_email_campaign_recipient(@recipient_id)
            """,
            connection);
        command.Parameters.AddWithValue("recipient_id", recipientId);

        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow,
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ClaimedDelivery(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4));
    }

    private async Task CompleteAsync(
        Guid recipientId,
        bool succeeded,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT public.complete_admin_email_campaign_recipient(@recipient_id, @succeeded, @error_message)",
            connection);
        command.Parameters.AddWithValue("recipient_id", recipientId);
        command.Parameters.AddWithValue("succeeded", succeeded);
        command.Parameters.AddWithValue(
            "error_message",
            errorMessage is null ? DBNull.Value : errorMessage);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string LimitError(string message)
    {
        var value = string.IsNullOrWhiteSpace(message)
            ? "E-posta saglayicisi gonderimi tamamlayamadi."
            : message.Trim();
        return value.Length <= 1000 ? value : value[..1000];
    }

    private sealed record ClaimedDelivery(
        Guid CampaignId,
        string Email,
        string? FullName,
        string Subject,
        string Message);
}
