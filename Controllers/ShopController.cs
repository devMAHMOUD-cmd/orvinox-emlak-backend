using System.Security.Claims;
using CraftoraApi.DTOs.Shop;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("general")]
public sealed class ShopController : ControllerBase
{
    private readonly IShopService _shopService;

    public ShopController(IShopService shopService)
    {
        _shopService = shopService ?? throw new ArgumentNullException(nameof(shopService));
    }

    [Authorize]
    [HttpPost]
    public IActionResult CreateShopAsync([FromBody] CreateShopDto dto)
    {
        throw new ConflictException(
            "Magaza, abonelik plani secilip odeme basariyla tamamlandiktan sonra olusturulur.");
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

    [Authorize]
    [HttpGet("me/followers")]
    public async Task<ActionResult<ShopFollowerListResponseDto>> GetMyShopFollowersAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30)
    {
        var result = await _shopService.GetMyShopFollowersAsync(GetCurrentUserId(), page, pageSize);
        return Ok(result);
    }

    [Authorize]
    [HttpGet("/api/shops/following")]
    public async Task<ActionResult<FollowedShopListResponseDto>> GetFollowedShopsAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _shopService.GetFollowedShopsAsync(GetCurrentUserId(), page, pageSize);
        return Ok(result);
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<PublicShopResponseDto>> GetShopBySlugAsync([FromRoute] string slug)
    {
        var result = await _shopService.GetShopBySlugAsync(slug, GetOptionalCurrentUserId());
        return Ok(result);
    }

    [HttpGet("{id:guid}/public")]
    public async Task<ActionResult<PublicShopResponseDto>> GetPublicShopByIdAsync([FromRoute] Guid id)
    {
        var result = await _shopService.GetPublicShopByIdAsync(id, GetOptionalCurrentUserId());
        return Ok(result);
    }

    [Authorize]
    [HttpPost("{id:guid}/follow")]
    public async Task<IActionResult> ToggleFollowAsync([FromRoute] Guid id)
    {
        var userId = GetCurrentUserId();
        var result = await _shopService.ToggleFollowAsync(id, userId);

        return Ok(result);
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

        return Ok(new { message = "Mağaza başarıyla pasife alındı." });
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

    private Guid? GetOptionalCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
    }
}
