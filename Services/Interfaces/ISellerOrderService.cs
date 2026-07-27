using CraftoraApi.DTOs.Order;

namespace CraftoraApi.Services.Interfaces;

public interface ISellerOrderService
{
    Task<SellerOrderListResponseDto> GetSellerOrdersAsync(
        Guid userId,
        string? status,
        DateTime? startDate,
        DateTime? endDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<SellerOrderDetailDto> GetSellerOrderDetailAsync(
        Guid userId,
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<SellerOrderSummaryDto> GetSellerOrderSummaryAsync(
        Guid userId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default);

    Task<RefundOrderResponseDto> RefundOrderAsync(
        Guid userId,
        Guid orderId,
        RefundOrderRequestDto request,
        CancellationToken cancellationToken = default);
}
