using System.Security.Claims;
using CraftoraApi.DTOs.Admin;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[EnableRateLimiting("general")]
[Route("api/admin/discovery/boosts")]
public sealed class AdminDiscoveryBoostController : ControllerBase
{
    private readonly IAdminDiscoveryBoostService _boostService;

    public AdminDiscoveryBoostController(IAdminDiscoveryBoostService boostService)
    {
        _boostService = boostService ?? throw new ArgumentNullException(nameof(boostService));
    }

    [HttpPost]
    public async Task<IActionResult> SetAsync(
        [FromBody] AdminDiscoveryBoostRequestDto request,
        CancellationToken cancellationToken)
    {
        return Ok(await _boostService.SetAsync(
            GetCurrentUserId(),
            request,
            cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> GetListAsync(CancellationToken cancellationToken)
    {
        return Ok(await _boostService.GetListAsync(
            GetCurrentUserId(),
            cancellationToken));
    }

    [HttpDelete("{boostId:guid}")]
    public async Task<IActionResult> StopAsync(
        [FromRoute] Guid boostId,
        CancellationToken cancellationToken)
    {
        await _boostService.StopAsync(
            GetCurrentUserId(),
            boostId,
            cancellationToken);
        return NoContent();
    }

    private Guid GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(value, out var userId))
        {
            throw new UnauthorizedException("Gecersiz kullanici token'i.");
        }

        return userId;
    }
}
