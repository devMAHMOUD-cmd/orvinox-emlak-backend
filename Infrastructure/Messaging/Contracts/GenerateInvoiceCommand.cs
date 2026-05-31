namespace CraftoraApi.Infrastructure.Messaging.Contracts;

public sealed record GenerateInvoiceCommand(
    Guid OrderId,
    Guid UserId,
    decimal Amount,
    string CustomerName,
    string CustomerEmail);
