using CraftoraApi.DTOs.Seller;

namespace CraftoraApi.Services.Interfaces;

public interface ISellerNotificationPreferenceService
{
    Task<SellerNotificationPreferencesDto> GetAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken = default);

    Task<SellerNotificationPreferencesDto> UpdateAsync(
        Guid sellerUserId,
        UpdateSellerNotificationPreferencesDto dto,
        CancellationToken cancellationToken = default);

    Task QueueTestOrderEmailAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken = default);

    Task<WeeklySellerReportPreviewResponseDto> QueueWeeklyReportPreviewAsync(
        Guid sellerUserId,
        WeeklySellerReportPreviewRequestDto request,
        CancellationToken cancellationToken = default);

    Task<bool> AreOrderEmailsEnabledAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken = default);
}
