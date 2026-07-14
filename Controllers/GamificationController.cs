using System.Security.Claims;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[ApiController]
[Route("api/gamification")]
[EnableRateLimiting("general")]
public sealed class GamificationController : ControllerBase
{
    private readonly IGamificationService _gamificationService;

    public GamificationController(IGamificationService gamificationService)
    {
        _gamificationService = gamificationService ?? throw new ArgumentNullException(nameof(gamificationService));
    }

    [AllowAnonymous]
    [HttpGet("leaderboard")]
    public async Task<IActionResult> GetLeaderboardAsync([FromQuery] int top = 50)
    {
        var result = await _gamificationService.GetLeaderboardAsync(top);
        return Ok(result);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMyWalletAsync()
    {
        var userId = GetCurrentUserId();
        var result = await _gamificationService.GetMyWalletAsync(userId);

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
