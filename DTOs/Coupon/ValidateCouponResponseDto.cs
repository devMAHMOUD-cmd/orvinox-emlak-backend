namespace CraftoraApi.DTOs.Coupon;

public sealed record ValidateCouponResponseDto(
    bool IsValid,
    string? ErrorMessage,
    decimal DiscountAmount,
    decimal FinalTotal);
