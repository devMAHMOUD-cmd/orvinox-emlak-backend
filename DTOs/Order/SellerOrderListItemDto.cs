namespace CraftoraApi.DTOs.Order;

public sealed record SellerOrderListItemDto(
    Guid OrderId,
    string OrderNumber,
    Guid ProductId,
    string ProductTitle,
    string? ProductCoverImagePublicUrl,
    string ProductType,
    Guid BuyerId,
    string? BuyerName,
    string BuyerEmail,
    decimal Amount,
    string? Currency,
    decimal? PlatformFee,
    decimal? SellerEarnings,
    string? PaymentStatus,
    string OrderStatus,
    DateTime? CreatedAt,
    DateTime? PaidAt,
    bool HasProductFile,
    string? ProductFileName,
    string? InvoicePdfUrl,
    DateTime? RefundedAt);
