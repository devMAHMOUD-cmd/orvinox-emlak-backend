using CraftoraApi.DTOs.Coupon;

namespace CraftoraApi.Services.Interfaces;

public interface ICouponService
{
    Task<CouponResponseDto> CreateCouponAsync(Guid userId, CreateCouponDto dto);

    Task<ValidateCouponResponseDto> ValidateCouponAsync(Guid userId, ValidateCouponRequestDto dto);

    Task RecordCouponUsageAsync(Guid userId, Guid couponId, Guid orderId);

    Task<CheckoutCouponResult> ResolveForCheckoutAsync(
        Guid userId,
        Guid productId,
        string code,
        decimal subtotalAmount);

    void AddUsage(Guid userId, Guid couponId, Guid orderId);
}

public sealed record CheckoutCouponResult(
    Guid CouponId,
    decimal DiscountAmount,
    decimal FinalTotal);
