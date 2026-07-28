using System.Security.Claims;
using CraftoraApi.DTOs.Coupon;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("general")]
[Route("api/coupons")]
public sealed class CouponController : ControllerBase
{
    private readonly ICouponService _couponService;

    public CouponController(ICouponService couponService)
    {
        _couponService = couponService ?? throw new ArgumentNullException(nameof(couponService));
    }

    [HttpPost]
    public async Task<IActionResult> CreateCouponAsync([FromBody] CreateCouponDto dto)
    {
        var userId = GetCurrentUserId();
        var result = await _couponService.CreateCouponAsync(userId, dto);

        return Ok(result);
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateCouponAsync([FromBody] ValidateCouponRequestDto dto)
    {
        var userId = GetCurrentUserId();
        var result = await _couponService.ValidateCouponAsync(userId, dto);

        return Ok(result);
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedException("Gecersiz kullanici token'i.");
        }

        return userId;
    }
}
