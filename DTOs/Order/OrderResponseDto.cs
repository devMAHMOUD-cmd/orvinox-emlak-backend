namespace CraftoraApi.DTOs.Order;

public sealed record OrderResponseDto(
    Guid Id,
    string OrderNumber,
    decimal Amount,
    string Status,
    DateTime? CreatedAt,
    string? InvoicePdfUrl);
