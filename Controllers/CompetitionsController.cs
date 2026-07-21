using System.Security.Claims;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CraftoraApi.Controllers;

[ApiController]
[Route("api/competitions")]
[EnableRateLimiting("general")]
public sealed class CompetitionsController : ControllerBase
{
    private readonly ICompetitionService _competitionService;

    public CompetitionsController(ICompetitionService competitionService)
    {
        _competitionService = competitionService ?? throw new ArgumentNullException(nameof(competitionService));
    }

    [AllowAnonymous]
    [HttpGet("active")]
    public async Task<IActionResult> GetActiveCompetitionAsync(CancellationToken cancellationToken = default)
    {
        var result = await _competitionService.GetActiveCompetitionAsync(
            GetOptionalCurrentUserId(),
            cancellationToken);

        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("active/leaderboard")]
    public async Task<IActionResult> GetActiveLeaderboardAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await _competitionService.GetActiveLeaderboardAsync(
            page,
            pageSize,
            cancellationToken);

        return Ok(result);
    }

    [Authorize]
    [HttpPost("active/join")]
    public async Task<IActionResult> JoinActiveCompetitionAsync(CancellationToken cancellationToken = default)
    {
        var result = await _competitionService.JoinActiveCompetitionAsync(
            GetCurrentUserId(),
            cancellationToken);

        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("history")]
    public async Task<IActionResult> GetHistoryAsync(
        [FromQuery] int months = 12,
        CancellationToken cancellationToken = default)
    {
        var result = await _competitionService.GetHistoryAsync(months, cancellationToken);
        return Ok(result);
    }

    [Authorize]
    [HttpGet("me/history")]
    public async Task<IActionResult> GetMyHistoryAsync(
        [FromQuery] int months = 12,
        CancellationToken cancellationToken = default)
    {
        var result = await _competitionService.GetMyHistoryAsync(
            GetCurrentUserId(),
            months,
            cancellationToken);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("{competitionId:guid}")]
    public async Task<IActionResult> GetCompetitionAsync(
        [FromRoute] Guid competitionId,
        CancellationToken cancellationToken = default)
    {
        var result = await _competitionService.GetCompetitionAsync(
            competitionId,
            GetOptionalCurrentUserId(),
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

    private Guid? GetOptionalCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(userIdClaim, out var userId)
            ? userId
            : null;
    }
}
