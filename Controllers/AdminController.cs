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
[Route("api/admin")]
public sealed class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;

    public AdminController(IAdminService adminService)
    {
        _adminService = adminService ?? throw new ArgumentNullException(nameof(adminService));
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        return Ok(await _adminService.GetOverviewAsync(cancellationToken));
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsersAsync(
        [FromQuery] string? query,
        [FromQuery] string? role,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _adminService.GetUsersAsync(query, role, status, page, pageSize, cancellationToken);
        return Ok(result);
    }

    [HttpGet("users/{userId:guid}")]
    public async Task<IActionResult> GetUserDetailAsync(
        [FromRoute] Guid userId,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _adminService.GetUserDetailAsync(userId, cancellationToken));
    }

    [HttpPost("users/{userId:guid}/warn")]
    public async Task<IActionResult> WarnUserAsync(
        [FromRoute] Guid userId,
        [FromBody] AdminWarnUserRequestDto dto,
        CancellationToken cancellationToken = default)
    {
        await _adminService.WarnUserAsync(GetCurrentUserId(), userId, dto, cancellationToken);
        return Ok(new { message = "Kullanici uyarildi." });
    }

    [HttpPost("users/{userId:guid}/lock")]
    public async Task<IActionResult> LockUserAsync(
        [FromRoute] Guid userId,
        [FromBody] AdminLockUserRequestDto dto,
        CancellationToken cancellationToken = default)
    {
        await _adminService.LockUserAsync(GetCurrentUserId(), userId, dto, cancellationToken);
        return Ok(new { message = "Kullanici kilitlendi." });
    }

    [HttpPost("users/{userId:guid}/unlock")]
    public async Task<IActionResult> UnlockUserAsync(
        [FromRoute] Guid userId,
        CancellationToken cancellationToken = default)
    {
        await _adminService.UnlockUserAsync(GetCurrentUserId(), userId, cancellationToken);
        return Ok(new { message = "Kullanici kilidi kaldirildi." });
    }

    [HttpPost("users/{userId:guid}/suspend")]
    public async Task<IActionResult> SuspendUserAsync(
        [FromRoute] Guid userId,
        [FromBody] AdminSuspendUserRequestDto dto,
        CancellationToken cancellationToken = default)
    {
        await _adminService.SuspendUserAsync(GetCurrentUserId(), userId, dto, cancellationToken);
        return Ok(new { message = "Kullanici askıya alindi." });
    }

    [HttpPost("users/{userId:guid}/restore")]
    public async Task<IActionResult> RestoreUserAsync(
        [FromRoute] Guid userId,
        CancellationToken cancellationToken = default)
    {
        await _adminService.RestoreUserAsync(GetCurrentUserId(), userId, cancellationToken);
        return Ok(new { message = "Kullanici geri alindi." });
    }

    [HttpDelete("users/{userId:guid}")]
    public async Task<IActionResult> SoftDeleteUserAsync(
        [FromRoute] Guid userId,
        CancellationToken cancellationToken = default)
    {
        await _adminService.SoftDeleteUserAsync(GetCurrentUserId(), userId, cancellationToken);
        return NoContent();
    }

    [HttpGet("reports")]
    public async Task<IActionResult> GetReportsAsync(
        [FromQuery] string? status,
        [FromQuery] string? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _adminService.GetReportsAsync(status, type, page, pageSize, cancellationToken));
    }

    [HttpPost("reports/{reportId:guid}/resolve")]
    public async Task<IActionResult> ResolveReportAsync(
        [FromRoute] Guid reportId,
        CancellationToken cancellationToken = default)
    {
        await _adminService.ResolveReportAsync(GetCurrentUserId(), reportId, cancellationToken);
        return Ok(new { message = "Rapor çözüldü." });
    }

    [HttpPost("reports/{reportId:guid}/reject")]
    public async Task<IActionResult> RejectReportAsync(
        [FromRoute] Guid reportId,
        CancellationToken cancellationToken = default)
    {
        await _adminService.RejectReportAsync(GetCurrentUserId(), reportId, cancellationToken);
        return Ok(new { message = "Rapor reddedildi." });
    }

    [HttpGet("competitions")]
    public async Task<IActionResult> GetCompetitionsAsync(CancellationToken cancellationToken = default)
    {
        return Ok(await _adminService.GetCompetitionsAsync(cancellationToken));
    }

    [HttpPost("competitions")]
    public async Task<IActionResult> CreateCompetitionAsync(
        [FromBody] AdminUpsertCompetitionDto dto,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _adminService.CreateCompetitionAsync(GetCurrentUserId(), dto, cancellationToken));
    }

    [HttpPut("competitions/{id:guid}")]
    public async Task<IActionResult> UpdateCompetitionAsync(
        [FromRoute] Guid id,
        [FromBody] AdminUpsertCompetitionDto dto,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _adminService.UpdateCompetitionAsync(GetCurrentUserId(), id, dto, cancellationToken));
    }

    [HttpPost("competitions/{id:guid}/start")]
    public async Task<IActionResult> StartCompetitionAsync(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        await _adminService.StartCompetitionAsync(GetCurrentUserId(), id, cancellationToken);
        return Ok(new { message = "Yarisma baslatildi." });
    }

    [HttpPost("competitions/{id:guid}/finish")]
    public async Task<IActionResult> FinishCompetitionAsync(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        await _adminService.FinishCompetitionAsync(GetCurrentUserId(), id, cancellationToken);
        return Ok(new { message = "Yarisma bitirildi." });
    }

    [HttpPost("competitions/{id:guid}/distribute-rewards")]
    public async Task<IActionResult> DistributeRewardsAsync(
        [FromRoute] Guid id,
        [FromBody] AdminDistributeRewardsRequestDto dto,
        CancellationToken cancellationToken = default)
    {
        await _adminService.DistributeRewardsAsync(GetCurrentUserId(), id, dto, cancellationToken);
        return Ok(new { message = "Oduller dagitildi." });
    }

    [HttpGet("pulse-news")]
    public async Task<IActionResult> GetAdminPulseNewsAsync(CancellationToken cancellationToken = default)
    {
        return Ok(await _adminService.GetPulseNewsAsync(includeUnpublished: true, cancellationToken));
    }

    [HttpPost("pulse-news")]
    public async Task<IActionResult> CreatePulseNewsAsync(
        [FromBody] UpsertPulseNewsDto dto,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _adminService.CreatePulseNewsAsync(GetCurrentUserId(), dto, cancellationToken));
    }

    [HttpPut("pulse-news/{id:guid}")]
    public async Task<IActionResult> UpdatePulseNewsAsync(
        [FromRoute] Guid id,
        [FromBody] UpsertPulseNewsDto dto,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _adminService.UpdatePulseNewsAsync(GetCurrentUserId(), id, dto, cancellationToken));
    }

    [HttpDelete("pulse-news/{id:guid}")]
    public async Task<IActionResult> DeletePulseNewsAsync(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        await _adminService.DeletePulseNewsAsync(GetCurrentUserId(), id, cancellationToken);
        return NoContent();
    }

    [HttpGet("home-cards")]
    public async Task<IActionResult> GetAdminHomeCardsAsync(CancellationToken cancellationToken = default)
    {
        return Ok(await _adminService.GetHomeCardsAsync(includeInactive: true, cancellationToken));
    }

    [HttpPut("home-cards")]
    public async Task<IActionResult> UpdateHomeCardsAsync(
        [FromBody] HomeCardsDto dto,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _adminService.UpdateHomeCardsAsync(GetCurrentUserId(), dto, cancellationToken));
    }

    [HttpGet("audit-logs")]
    public async Task<IActionResult> GetAuditLogsAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _adminService.GetAuditLogsAsync(page, pageSize, cancellationToken));
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedException("Gecersiz admin token'i.");
        }

        return userId;
    }
}
