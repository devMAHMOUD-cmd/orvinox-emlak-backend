using System.Security.Claims;
using CraftoraApi.DTOs.Seller;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[ApiController]
[Authorize(Policy = "SellerOnly")]
[EnableRateLimiting("general")]
[Route("api/seller/notification-preferences")]
public sealed class SellerNotificationPreferencesController : ControllerBase
{
    private readonly ISellerNotificationPreferenceService _preferenceService;

    public SellerNotificationPreferencesController(
        ISellerNotificationPreferenceService preferenceService)
    {
        _preferenceService = preferenceService ?? throw new ArgumentNullException(nameof(preferenceService));
    }

    [HttpGet]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var result = await _preferenceService.GetAsync(
            GetCurrentUserId(),
            cancellationToken);

        return Ok(result);
    }

    [HttpPut]
    public async Task<IActionResult> UpdateAsync(
        [FromBody] SellerNotificationPreferencesDto request,
        CancellationToken cancellationToken)
    {
        var result = await _preferenceService.UpdateAsync(
            GetCurrentUserId(),
            request,
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("test-order-email")]
    [EnableRateLimiting("seller-email-test")]
    public async Task<IActionResult> TestOrderEmailAsync(CancellationToken cancellationToken)
    {
        await _preferenceService.QueueTestOrderEmailAsync(
            GetCurrentUserId(),
            cancellationToken);

        return Ok(new TestSellerEmailResponseDto("Test e-postası gönderim kuyruğuna alındı."));
    }

    [HttpPost("test-weekly-report")]
    [EnableRateLimiting("seller-email-test")]
    public async Task<IActionResult> TestWeeklyReportAsync(
        [FromBody] WeeklySellerReportPreviewRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _preferenceService.QueueWeeklyReportPreviewAsync(
            GetCurrentUserId(),
            request,
            cancellationToken);

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
