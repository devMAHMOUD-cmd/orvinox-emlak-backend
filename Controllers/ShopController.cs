using System.Security.Claims;
using CraftoraApi.DTOs.Shop;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CraftoraApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ShopController : ControllerBase
{
    private readonly IShopService _shopService;

    public ShopController(IShopService shopService)
    {
        _shopService = shopService ?? throw new ArgumentNullException(nameof(shopService));
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreateShopAsync([FromBody] CreateShopDto dto)
    {
        var userId = GetCurrentUserId();
        var result = await _shopService.CreateShopAsync(userId, dto);

        return Ok(result);
    }

    [Authorize]
    [HttpPut]
    public async Task<IActionResult> UpdateShopAsync([FromBody] UpdateShopDto dto)
    {
        var userId = GetCurrentUserId();
        var result = await _shopService.UpdateShopAsync(userId, dto);

        return Ok(result);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMyShopAsync()
    {
        var userId = GetCurrentUserId();
        var result = await _shopService.GetMyShopAsync(userId);

        return Ok(result);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> GetShopBySlugAsync([FromRoute] string slug)
    {
        var result = await _shopService.GetShopBySlugAsync(slug);
        return Ok(result);
    }

    [Authorize]
    [HttpPost("{id:guid}/follow")]
    public async Task<IActionResult> ToggleFollowAsync([FromRoute] Guid id)
    {
        var userId = GetCurrentUserId();
        await _shopService.ToggleFollowAsync(id, userId);

        return Ok(new { message = "Takip durumu güncellendi." });
    }

    [Authorize]
    [HttpGet("{id:guid}/analytics")]
    [HttpGet("/api/shops/{id:guid}/analytics")]
    public async Task<IActionResult> GetTrafficReportAsync(
        [FromRoute] Guid id,
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate)
    {
        var userId = GetCurrentUserId();
        var result = await _shopService.GetShopTrafficReportAsync(id, userId, startDate, endDate);

        return Ok(result);
    }

    [Authorize]
    [HttpDelete("{id:guid}")]
    [HttpDelete("/api/shops/{id:guid}")]
    public async Task<IActionResult> DeleteShopAsync([FromRoute] Guid id)
    {
        var userId = GetCurrentUserId();
        await _shopService.DeleteShopAsync(id, userId);

        return Ok(new { message = "MaÄŸaza baÅŸarÄ±yla pasife alÄ±ndÄ±." });
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
