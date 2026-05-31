using CraftoraApi.Infrastructure.Messaging.Contracts;

namespace CraftoraApi.Infrastructure.Services;

public interface IPdfService
{
    Task<byte[]> GenerateInvoicePdfAsync(
        GenerateInvoiceCommand command,
        CancellationToken cancellationToken = default);
}
