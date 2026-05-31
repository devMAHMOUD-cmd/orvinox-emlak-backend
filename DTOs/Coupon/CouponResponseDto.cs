namespace CraftoraApi.DTOs.Coupon;

public sealed record CouponResponseDto(
    Guid Id,
    string Code,
    string DiscountType,
    decimal DiscountValue,
    DateTime? ExpirationDate,
    bool IsActive);
