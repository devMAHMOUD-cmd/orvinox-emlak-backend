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
[Route("api/admin/email-campaigns")]
public sealed class AdminEmailCampaignController : ControllerBase
{
    private readonly IAdminEmailCampaignService _campaignService;

    public AdminEmailCampaignController(IAdminEmailCampaignService campaignService)
    {
        _campaignService = campaignService;
    }

    [HttpPost("preview")]
    public async Task<IActionResult> PreviewAsync(
        [FromBody] AdminEmailCampaignPreviewRequestDto request,
        CancellationToken cancellationToken)
    {
        return Ok(await _campaignService.PreviewAsync(request, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> CreateAsync(
        [FromBody] AdminEmailCampaignSendRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _campaignService.CreateAndDispatchAsync(
            GetCurrentUserId(),
            request,
            cancellationToken);
        return AcceptedAtRoute(
            "GetAdminEmailCampaign",
            new { campaignId = result.Id },
            result);
    }

    [HttpGet("{campaignId:guid}", Name = "GetAdminEmailCampaign")]
    public async Task<IActionResult> GetAsync(
        [FromRoute] Guid campaignId,
        CancellationToken cancellationToken)
    {
        return Ok(await _campaignService.GetAsync(
            GetCurrentUserId(),
            campaignId,
            cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> GetListAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _campaignService.GetListAsync(
            GetCurrentUserId(),
            page,
            pageSize,
            cancellationToken));
    }

    [HttpPost("{campaignId:guid}/retry-failed")]
    public async Task<IActionResult> RetryFailedAsync(
        [FromRoute] Guid campaignId,
        CancellationToken cancellationToken)
    {
        return Ok(await _campaignService.RetryFailedAsync(
            GetCurrentUserId(),
            campaignId,
            cancellationToken));
    }

    private Guid GetCurrentUserId()
    {
        var rawUserId = User.FindFirstValue("sub")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(rawUserId, out var userId)
            ? userId
            : throw new UnauthorizedException("Gecersiz kullanici kimligi.");
    }
}
