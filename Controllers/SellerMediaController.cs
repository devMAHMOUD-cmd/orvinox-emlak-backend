using System.Security.Claims;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CraftoraApi.Controllers;

[ApiController]
[Authorize]
[Route("api/seller/media")]
public sealed class SellerMediaController : ControllerBase
{
    private readonly IMediaService _mediaService;

    public SellerMediaController(IMediaService mediaService)
    {
        _mediaService = mediaService ?? throw new ArgumentNullException(nameof(mediaService));
    }

    [HttpGet]
    public async Task<IActionResult> GetMyMediaAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12)
    {
        var result = await _mediaService.GetMyMediaAsync(GetCurrentUserId(), page, pageSize);
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
