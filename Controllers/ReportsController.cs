using System.Security.Claims;
using CraftoraApi.DTOs.Report;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using CraftoraApi.Data;
using CraftoraApi.Infrastructure.Security;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Enums;

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

    [Authorize]
    [EnableRateLimiting("general")]
    [HttpPost]
    public async Task<IActionResult> CreateReportAsync(
        [FromBody] CreateReportDto dto,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.TargetType) ||
            string.IsNullOrWhiteSpace(dto.Reason))
        {
            throw new BadRequestException("Sikayet hedef tipi ve sebebi zorunludur.");
        }

        if (dto.TargetId == Guid.Empty)
        {
            throw new BadRequestException("Gecerli bir sikayet hedefi zorunludur.");
        }

        var targetType = Normalize(dto.TargetType);
        var reason = Normalize(dto.Reason);
        var description = PlainTextInputValidator.Optional(
            dto.Description,
            "Sikayet aciklamasi",
            5000);

        if (!AllowedTargetTypes.Contains(targetType))
        {
            return BadRequest(new { message = "Gecersiz sikayet hedef tipi." });
        }

        if (!AllowedReasons.Contains(reason))
        {
            return BadRequest(new { message = "Gecersiz sikayet sebebi." });
        }

        var reportId = Guid.NewGuid();
        var reportedByUserId = GetCurrentUserId();
        var createdAt = DateTime.UtcNow;
        var target = await ResolveTargetAsync(targetType, dto.TargetId, cancellationToken);
        if (!target.Exists)
        {
            throw new NotFoundException("Sikayet hedefi bulunamadi.");
        }

        if (target.OwnerUserId == reportedByUserId)
        {
            throw new BadRequestException("Kendi hesabinizi veya iceriginizi sikayet edemezsiniz.");
        }

        var hasOpenReport = await _dbContext.Database
            .SqlQueryRaw<int>(
                """
                SELECT 1
                FROM admin_reports
                WHERE reported_by_user_id = {0}
                  AND type = {1}
                  AND target_id = {2}
                  AND status IN ('open', 'pending')
                LIMIT 1
                """,
                reportedByUserId,
                targetType,
                dto.TargetId)
            .AnyAsync(cancellationToken);

        if (hasOpenReport)
        {
            return Conflict(new { message = "Bu icerik icin zaten acik bir sikayetiniz var." });
        }

        var insertedRows = await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO admin_reports (
                id,
                type,
                target_id,
                target_title,
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
                {target.Title},
                {reportedByUserId},
                {reason},
                {description},
                {"open"},
                {createdAt},
                {createdAt}
            )
            ON CONFLICT (reported_by_user_id, type, target_id)
                WHERE status IN ('open', 'pending', 'reviewing')
                DO NOTHING
            """, cancellationToken);

        if (insertedRows == 0)
        {
            return Conflict(new { message = "Bu icerik icin zaten acik bir sikayetiniz var." });
        }

        return Ok(new ReportCreatedDto(reportId, "open", createdAt));
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

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private async Task<ReportTarget> ResolveTargetAsync(
        string targetType,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        switch (targetType)
        {
            case "product":
            {
                var target = await _dbContext.Products
                    .AsNoTracking()
                    .Where(item => item.Id == targetId)
                    .Select(item => new { item.Title, OwnerUserId = item.Shop.UserId })
                    .SingleOrDefaultAsync(cancellationToken);
                return target is null
                    ? ReportTarget.Missing
                    : new(true, target.OwnerUserId, target.Title);
            }
            case "course":
            {
                var course = await _dbContext.Courses
                    .AsNoTracking()
                    .Where(item => item.Id == targetId)
                    .Select(item => new
                    {
                        item.Product.Title,
                        OwnerUserId = item.Product.Shop.UserId
                    })
                    .SingleOrDefaultAsync(cancellationToken);
                if (course is not null)
                {
                    return new(true, course.OwnerUserId, course.Title);
                }

                var product = await _dbContext.Products
                    .AsNoTracking()
                    .Where(item => item.Id == targetId && item.Type == ProductType.Course)
                    .Select(item => new { item.Title, OwnerUserId = item.Shop.UserId })
                    .SingleOrDefaultAsync(cancellationToken);
                return product is null
                    ? ReportTarget.Missing
                    : new(true, product.OwnerUserId, product.Title);
            }
            case "media":
            {
                var target = await _dbContext.Media
                    .AsNoTracking()
                    .Where(item => item.Id == targetId)
                    .Select(item => new
                    {
                        Title = item.Caption,
                        OwnerUserId = item.Shop.UserId
                    })
                    .SingleOrDefaultAsync(cancellationToken);
                return target is null
                    ? ReportTarget.Missing
                    : new(true, target.OwnerUserId, target.Title);
            }
            case "shop":
            {
                var target = await _dbContext.Shops
                    .AsNoTracking()
                    .Where(item => item.Id == targetId)
                    .Select(item => new { Title = item.ShopName, OwnerUserId = item.UserId })
                    .SingleOrDefaultAsync(cancellationToken);
                return target is null
                    ? ReportTarget.Missing
                    : new(true, target.OwnerUserId, target.Title);
            }
            case "user":
            {
                var target = await _dbContext.Users
                    .AsNoTracking()
                    .Where(item => item.Id == targetId)
                    .Select(item => new { Title = item.FullName ?? item.Email, OwnerUserId = item.Id })
                    .SingleOrDefaultAsync(cancellationToken);
                return target is null
                    ? ReportTarget.Missing
                    : new(true, target.OwnerUserId, target.Title);
            }
            case "comment":
            {
                var target = await _dbContext.MediaComments
                    .AsNoTracking()
                    .Where(item => item.Id == targetId)
                    .Select(item => new { Title = item.CommentText, OwnerUserId = item.UserId })
                    .SingleOrDefaultAsync(cancellationToken);
                return target is null
                    ? ReportTarget.Missing
                    : new(true, target.OwnerUserId, target.Title);
            }
            default:
                return ReportTarget.Missing;
        }
    }

    private sealed record ReportTarget(bool Exists, Guid OwnerUserId, string? Title)
    {
        public static ReportTarget Missing { get; } = new(false, Guid.Empty, null);
    }
}
