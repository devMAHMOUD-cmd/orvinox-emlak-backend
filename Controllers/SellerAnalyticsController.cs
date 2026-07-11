using System.Security.Claims;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[ApiController]
[Authorize(Policy = "SellerOnly")]
[EnableRateLimiting("general")]
[Route("api/seller/analytics")]
public sealed class SellerAnalyticsController : ControllerBase
{
    private readonly ISellerAnalyticsService _sellerAnalyticsService;

    public SellerAnalyticsController(ISellerAnalyticsService sellerAnalyticsService)
    {
        _sellerAnalyticsService = sellerAnalyticsService ?? throw new ArgumentNullException(nameof(sellerAnalyticsService));
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverviewAsync(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var result = await _sellerAnalyticsService.GetOverviewAsync(
            GetCurrentUserId(),
            startDate,
            endDate,
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("funnel")]
    public async Task<IActionResult> GetFunnelAsync(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var result = await _sellerAnalyticsService.GetFunnelAsync(
            GetCurrentUserId(),
            startDate,
            endDate,
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("traffic-sources")]
    public async Task<IActionResult> GetTrafficSourcesAsync(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var result = await _sellerAnalyticsService.GetTrafficSourcesAsync(
            GetCurrentUserId(),
            startDate,
            endDate,
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("top-products")]
    public async Task<IActionResult> GetTopProductsAsync(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _sellerAnalyticsService.GetTopProductsAsync(
            GetCurrentUserId(),
            startDate,
            endDate,
            limit,
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("courses")]
    public async Task<IActionResult> GetCoursesAsync(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var result = await _sellerAnalyticsService.GetCoursesAsync(
            GetCurrentUserId(),
            startDate,
            endDate,
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("courses/{courseId:guid}")]
    public async Task<IActionResult> GetCourseDetailAsync(
        [FromRoute] Guid courseId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var result = await _sellerAnalyticsService.GetCourseDetailAsync(
            GetCurrentUserId(),
            courseId,
            startDate,
            endDate,
            cancellationToken);

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
