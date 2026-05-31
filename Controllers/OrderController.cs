using System.Security.Claims;
using CraftoraApi.DTOs.Order;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CraftoraApi.Controllers;

[ApiController]
[Authorize]
[Route("api/orders")]
public sealed class OrderController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrderController(IOrderService orderService)
    {
        _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
    }

    [Authorize(Roles = "user")]
    [HttpPost("checkout")]
    public async Task<IActionResult> CheckoutAsync([FromBody] CheckoutRequestDto request)
    {
        var buyerId = GetCurrentUserId();
        var result = await _orderService.CheckoutCartAsync(buyerId, request);

        return Ok(result);
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMyOrdersAsync()
    {
        var buyerId = GetCurrentUserId();
        var result = await _orderService.GetMyOrdersAsync(buyerId);

        return Ok(result);
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedException("Geçersiz kullanıcı token'ı.");
        }

        return userId;
    }
}
