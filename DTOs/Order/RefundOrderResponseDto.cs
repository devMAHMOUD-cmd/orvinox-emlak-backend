namespace CraftoraApi.DTOs.Order;

public sealed record RefundOrderResponseDto(
    Guid OrderId,
    string Status,
    decimal RefundedAmount,
    string ProviderRefundId,
    DateTime RefundedAt);
