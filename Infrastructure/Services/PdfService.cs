using CraftoraApi.Infrastructure.Messaging.Contracts;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace CraftoraApi.Infrastructure.Services;

public sealed class PdfService : IPdfService
{
    public Task<byte[]> GenerateInvoicePdfAsync(
        GenerateInvoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        QuestPDF.Settings.License = LicenseType.Community;

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(40);
                page.Content().Column(column =>
                {
                    column.Item().Text("Craftora Invoice").FontSize(24).Bold();
                    column.Item().Text($"Order ID: {command.OrderId}");
                    column.Item().Text($"Customer: {command.CustomerName}");
                    column.Item().Text($"Email: {command.CustomerEmail}");
                    column.Item().Text($"Amount: {command.Amount:0.00} USD");
                    column.Item().Text($"Generated At: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
                });
            });
        }).GeneratePdf();

        return Task.FromResult(pdfBytes);
    }
}
