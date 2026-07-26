namespace CraftoraApi.DTOs.Order;

public sealed record OrderResponseDto(
    Guid Id,
    string OrderNumber,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    decimal Amount,
    string Status,
    DateTime? CreatedAt,
    string? InvoicePdfUrl);
