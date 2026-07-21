namespace CraftoraApi.Services.Interfaces;

using CraftoraApi.DTOs.Seller;

public interface IWeeklySellerReportService
{
    Task QueueWeeklyReportsAsync(
        DateTime startDateUtc,
        DateTime endDateUtc,
        Guid? sellerUserId = null,
        CancellationToken cancellationToken = default);

    Task<WeeklySellerReportPreviewResponseDto> GenerateAndQueueWeeklyReportAsync(
        Guid sellerUserId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken = default);
}
