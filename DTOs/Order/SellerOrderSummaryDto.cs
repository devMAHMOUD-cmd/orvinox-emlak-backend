namespace CraftoraApi.DTOs.Order;

public sealed record SellerOrderSummaryDto(
    int TotalOrders,
    int PaidOrders,
    int FailedOrders,
    int RefundedOrders,
    decimal TotalRevenue,
    decimal PendingAmount,
    decimal AverageOrderValue);
