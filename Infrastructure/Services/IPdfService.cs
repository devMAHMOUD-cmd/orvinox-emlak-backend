using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.DTOs.Admin;
using CraftoraApi.DTOs.Seller;

namespace CraftoraApi.Infrastructure.Services;

public interface IPdfService
{
    Task<byte[]> GenerateInvoicePdfAsync(
        GenerateInvoiceCommand command,
        CancellationToken cancellationToken = default);

    Task<byte[]> GenerateWeeklySellerReportPdfAsync(
        WeeklySellerReportData report,
        CancellationToken cancellationToken = default);

    Task<byte[]> GenerateCompetitionCertificatePdfAsync(
        CompetitionCertificateData certificate,
        CancellationToken cancellationToken = default);
}
