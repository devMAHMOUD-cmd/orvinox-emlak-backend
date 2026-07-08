using System.Security.Claims;
using CraftoraApi.DTOs.Report;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CraftoraApi.Data;

namespace CraftoraApi.Controllers;

[ApiController]
[Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    private static readonly HashSet<string> AllowedTargetTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "user",
        "shop",
        "product",
        "media",
        "course",
        "comment"
    };

    private static readonly HashSet<string> AllowedReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "spam",
        "abuse",
        "copyright",
        "scam",
        "other"
    };

    private readonly AppDbContext _dbContext;

    public ReportsController(AppDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> CreateReportAsync(
        [FromBody] CreateReportDto dto,
        CancellationToken cancellationToken = default)
    {
        var targetType = Normalize(dto.TargetType);
        var reason = Normalize(dto.Reason);

        if (!AllowedTargetTypes.Contains(targetType))
        {
            return BadRequest(new { message = "Gecersiz sikayet hedef tipi." });
        }

        if (!AllowedReasons.Contains(reason))
        {
            return BadRequest(new { message = "Gecersiz sikayet sebebi." });
        }

        var reportId = Guid.NewGuid();
        var reportedByUserId = TryGetCurrentUserId();
        var createdAt = DateTime.UtcNow;

        await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO admin_reports (
                id,
                type,
                target_id,
                reported_by_user_id,
                reason,
                description,
                status,
                created_at,
                updated_at
            )
            VALUES (
                {reportId},
                {targetType},
                {dto.TargetId},
                {reportedByUserId},
                {reason},
                {dto.Description},
                {"open"},
                {createdAt},
                {createdAt}
            )
            """, cancellationToken);

        return Ok(new ReportCreatedDto(reportId, "open", createdAt));
    }

    private Guid? TryGetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
    }
}
