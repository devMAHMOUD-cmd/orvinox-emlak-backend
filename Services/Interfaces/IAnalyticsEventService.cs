using System.Net;
using CraftoraApi.DTOs.Analytics;

namespace CraftoraApi.Services.Interfaces;

public interface IAnalyticsEventService
{
    Task<AnalyticsEventResponseDto> TrackAsync(
        TrackAnalyticsEventDto dto,
        Guid? userId,
        IPAddress? ipAddress,
        string? userAgent,
        string? fallbackReferrer,
        CancellationToken cancellationToken = default);

    Task TrackAddToCartAsync(Guid productId, Guid userId, CancellationToken cancellationToken = default);

    Task TrackCheckoutStartedAsync(Guid productId, Guid userId, CancellationToken cancellationToken = default);

    Task TrackPurchaseCompletedAsync(Guid orderId, Guid userId, CancellationToken cancellationToken = default);

    Task TrackDownloadClickedAsync(Guid productId, Guid userId, CancellationToken cancellationToken = default);
}
