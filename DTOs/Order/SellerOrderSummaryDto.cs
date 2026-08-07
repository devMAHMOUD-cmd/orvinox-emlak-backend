namespace CraftoraApi.DTOs.Order;

public sealed record SellerOrderSummaryDto(
    int TotalOrders,
    int PaidOrders,
    int FailedOrders,
    int RefundedOrders,
    decimal TotalRevenue,
    decimal PendingAmount,
    decimal AverageOrderValue,
    IReadOnlyList<SellerOrderCurrencySummaryDto> TotalsByCurrency);

public sealed record SellerOrderCurrencySummaryDto(
    string Currency,
    int PaidOrders,
    decimal TotalRevenue,
    decimal PendingAmount,
    decimal AverageOrderValue);
