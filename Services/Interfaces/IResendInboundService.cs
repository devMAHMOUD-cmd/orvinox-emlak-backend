namespace CraftoraApi.Services.Interfaces;

public interface IResendInboundService
{
    Task<ResendInboundResult> ProcessAsync(
        string svixId,
        ResendReceivedWebhook webhook,
        CancellationToken cancellationToken = default);
}

public sealed record ResendInboundResult(
    string Status,
    Guid? TicketId = null);

public sealed record ResendReceivedWebhook(
    string Type,
    Guid EmailId,
    string From,
    IReadOnlyList<string> To,
    IReadOnlyList<string> ReceivedFor,
    string? Subject);
