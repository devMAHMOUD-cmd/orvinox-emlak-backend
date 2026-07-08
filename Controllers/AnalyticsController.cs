using System.Security.Claims;
using CraftoraApi.DTOs.Analytics;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CraftoraApi.Controllers;

[ApiController]
[Route("api/analytics")]
public sealed class AnalyticsController : ControllerBase
{
    private readonly IAnalyticsEventService _analyticsEventService;

    public AnalyticsController(IAnalyticsEventService analyticsEventService)
    {
        _analyticsEventService = analyticsEventService ?? throw new ArgumentNullException(nameof(analyticsEventService));
    }

    [HttpPost("events")]
    [AllowAnonymous]
    public async Task<IActionResult> TrackEvent([FromBody] TrackAnalyticsEventDto dto, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var userAgent = Request.Headers.UserAgent.ToString();
        var referrer = Request.Headers.Referer.ToString();

        var result = await _analyticsEventService.TrackAsync(
            dto,
            userId,
            HttpContext.Connection.RemoteIpAddress,
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            string.IsNullOrWhiteSpace(referrer) ? null : referrer,
            cancellationToken);

        return Ok(result);
    }

    private Guid? GetCurrentUserId()
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        return Guid.TryParse(userIdValue, out var userId)
            ? userId
            : null;
    }
}
