using CraftoraApi.DTOs.Coupon;

namespace CraftoraApi.Services.Interfaces;

public interface ICouponService
{
    Task<CouponResponseDto> CreateCouponAsync(Guid userId, CreateCouponDto dto);

    Task<ValidateCouponResponseDto> ValidateCouponAsync(Guid userId, ValidateCouponRequestDto dto);

    Task RecordCouponUsageAsync(Guid userId, Guid couponId, Guid orderId);
}
