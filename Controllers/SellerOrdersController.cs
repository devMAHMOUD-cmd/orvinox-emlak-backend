using System.Security.Claims;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("general")]
[Route("api/seller/orders")]
public sealed class SellerOrdersController : ControllerBase
{
    private readonly ISellerOrderService _sellerOrderService;

    public SellerOrdersController(ISellerOrderService sellerOrderService)
    {
        _sellerOrderService = sellerOrderService ?? throw new ArgumentNullException(nameof(sellerOrderService));
    }

    [HttpGet]
    public async Task<IActionResult> GetSellerOrdersAsync(
        [FromQuery] string? status,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sellerOrderService.GetSellerOrdersAsync(
            GetCurrentUserId(),
            status,
            startDate,
            endDate,
            page,
            pageSize,
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSellerOrderSummaryAsync(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken = default)
    {
        var result = await _sellerOrderService.GetSellerOrderSummaryAsync(
            GetCurrentUserId(),
            startDate,
            endDate,
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("{orderId:guid}")]
    public async Task<IActionResult> GetSellerOrderDetailAsync(
        [FromRoute] Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var result = await _sellerOrderService.GetSellerOrderDetailAsync(
            GetCurrentUserId(),
            orderId,
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
