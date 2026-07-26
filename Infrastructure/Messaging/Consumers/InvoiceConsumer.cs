using CraftoraApi.Data;
using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.Infrastructure.Services;
using CraftoraApi.Services.Interfaces;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Infrastructure.Messaging.Consumers;

public sealed class InvoiceConsumer : IConsumer<GenerateInvoiceCommand>
{
    private readonly IPdfService _pdfService;
    private readonly IStorageService _storageService;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<InvoiceConsumer> _logger;

    public InvoiceConsumer(
        IPdfService pdfService,
        IStorageService storageService,
        AppDbContext dbContext,
        ILogger<InvoiceConsumer> logger)
    {
        _pdfService = pdfService ?? throw new ArgumentNullException(nameof(pdfService));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Consume(ConsumeContext<GenerateInvoiceCommand> context)
    {
        var message = context.Message;
        var pdfBytes = await _pdfService.GenerateInvoicePdfAsync(message, context.CancellationToken);
        var objectKey = $"invoices/{message.OrderId}.pdf";

        await _storageService.UploadFileAsync(
            "invoices",
            objectKey,
            pdfBytes,
            "application/pdf",
            context.CancellationToken);

        var invoiceUrl = _storageService.GeneratePresignedDownloadUrl(
            "invoices",
            objectKey,
            60 * 24 * 7);

        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             SELECT public.set_order_invoice_url(
                 {message.OrderId},
                 {invoiceUrl})
             """,
            context.CancellationToken);

        await context.Publish(new SendEmailCommand(
            To: message.CustomerEmail,
            Subject: "Craftora faturanız hazır",
            Body: $"Merhaba {message.CustomerName}, faturanız hazır: {invoiceUrl}",
            IsHtml: false), context.CancellationToken);

        _logger.LogInformation(
            "Invoice generated and email command published. OrderId: {OrderId}, UserId: {UserId}",
            message.OrderId,
            message.UserId);
    }
}
