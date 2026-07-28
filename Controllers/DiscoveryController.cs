using System.Security.Claims;
using CraftoraApi.DTOs.Discovery;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[ApiController]
[Authorize]
[Route("api/discovery")]
public sealed class DiscoveryController : ControllerBase
{
    private readonly IDiscoveryEventService _discoveryEventService;

    public DiscoveryController(IDiscoveryEventService discoveryEventService)
    {
        _discoveryEventService = discoveryEventService
            ?? throw new ArgumentNullException(nameof(discoveryEventService));
    }

    [HttpPost("events")]
    [EnableRateLimiting("general")]
    public async Task<IActionResult> RecordEventsAsync(
        [FromBody] DiscoveryEventBatchRequestDto request,
        CancellationToken cancellationToken)
    {
        var response = await _discoveryEventService.RecordBatchAsync(
            GetCurrentUserId(),
            request,
            cancellationToken);
        return Accepted(response);
    }

    [HttpPost("feedback")]
    [EnableRateLimiting("general")]
    public async Task<IActionResult> SetFeedbackAsync(
        [FromBody] DiscoveryFeedbackRequestDto request,
        CancellationToken cancellationToken)
    {
        var response = await _discoveryEventService.SetFeedbackAsync(
            GetCurrentUserId(),
            request,
            cancellationToken);
        return Ok(response);
    }

    [HttpGet("feedback")]
    public async Task<IActionResult> GetFeedbackAsync(CancellationToken cancellationToken)
    {
        return Ok(await _discoveryEventService.GetFeedbackAsync(
            GetCurrentUserId(),
            cancellationToken));
    }

    [HttpDelete("feedback/{id:guid}")]
    public async Task<IActionResult> RemoveFeedbackAsync(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        await _discoveryEventService.RemoveFeedbackAsync(
            GetCurrentUserId(),
            id,
            cancellationToken);
        return NoContent();
    }

    private Guid GetCurrentUserId()
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            throw new UnauthorizedException("Gecersiz kullanici token'i.");
        }

        return userId;
    }
}
