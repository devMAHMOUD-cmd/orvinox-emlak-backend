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
[Route("api/seller/customers")]
public sealed class SellerCustomersController : ControllerBase
{
    private readonly ISellerCustomerService _sellerCustomerService;

    public SellerCustomersController(ISellerCustomerService sellerCustomerService)
    {
        _sellerCustomerService = sellerCustomerService ?? throw new ArgumentNullException(nameof(sellerCustomerService));
    }

    [HttpGet]
    public async Task<IActionResult> GetCustomersAsync(
        [FromQuery] string? type,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _sellerCustomerService.GetCustomersAsync(
            GetCurrentUserId(),
            type,
            startDate,
            endDate,
            page,
            pageSize,
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummaryAsync(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken = default)
    {
        var result = await _sellerCustomerService.GetSummaryAsync(
            GetCurrentUserId(),
            startDate,
            endDate,
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("segments")]
    public async Task<IActionResult> GetSegmentsAsync(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken = default)
    {
        var result = await _sellerCustomerService.GetSegmentsAsync(
            GetCurrentUserId(),
            startDate,
            endDate,
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("{customerId:guid}")]
    public async Task<IActionResult> GetCustomerDetailAsync(
        [FromRoute] Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var result = await _sellerCustomerService.GetCustomerDetailAsync(
            GetCurrentUserId(),
            customerId,
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
