using System.Security.Claims;
using CraftoraApi.DTOs.Subscription;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CraftoraApi.Controllers;

[ApiController]
[Authorize]
[Route("api/subscriptions")]
public sealed class SubscriptionController : ControllerBase
{
    private readonly ISubscriptionService _subscriptionService;

    public SubscriptionController(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
    }

    [Authorize(Policy = "SellerOnly")]
    [HttpGet("me")]
    public async Task<IActionResult> GetMySubscriptionAsync()
    {
        var userId = GetCurrentUserId();
        var result = await _subscriptionService.GetMySubscriptionAsync(userId);

        return Ok(result);
    }

    [Authorize]
    [HttpPost("start")]
    public async Task<IActionResult> StartSubscriptionAsync([FromBody] StartSubscriptionRequestDto request)
    {
        var userId = GetCurrentUserId();
        var result = await _subscriptionService.StartSubscriptionAsync(userId, request);

        return Ok(result);
    }

    [Authorize(Policy = "SellerOnly")]
    [HttpPost("cancel")]
    public async Task<IActionResult> CancelSubscriptionAsync()
    {
        var userId = GetCurrentUserId();
        var result = await _subscriptionService.CancelSubscriptionAsync(userId);

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
