using System.Security.Claims;
using CraftoraApi.DTOs.Cart;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("general")]
[Route("api/cart")]
public sealed class CartController : ControllerBase
{
    private readonly ICartService _cartService;

    public CartController(ICartService cartService)
    {
        _cartService = cartService ?? throw new ArgumentNullException(nameof(cartService));
    }

    [HttpGet]
    public async Task<IActionResult> GetUserCartAsync()
    {
        var userId = GetCurrentUserId();
        var result = await _cartService.GetUserCartAsync(userId);

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> AddToCartAsync([FromBody] AddToCartDto dto)
    {
        var userId = GetCurrentUserId();
        var result = await _cartService.AddToCartAsync(userId, dto);

        return Ok(result);
    }

    [HttpPut("items/{id:guid}")]
    public async Task<IActionResult> UpdateCartItemQuantityAsync(
        [FromRoute] Guid id,
        [FromBody] UpdateCartItemDto dto)
    {
        var userId = GetCurrentUserId();
        var result = await _cartService.UpdateCartItemQuantityAsync(userId, id, dto.Quantity);

        return Ok(result);
    }

    [HttpDelete("items/{id:guid}")]
    public async Task<IActionResult> RemoveFromCartAsync([FromRoute] Guid id)
    {
        var userId = GetCurrentUserId();
        await _cartService.RemoveFromCartAsync(userId, id);

        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> ClearCartAsync()
    {
        var userId = GetCurrentUserId();
        await _cartService.ClearCartAsync(userId);

        return NoContent();
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
