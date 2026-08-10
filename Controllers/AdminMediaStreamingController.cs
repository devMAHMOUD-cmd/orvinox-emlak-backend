using CraftoraApi.Data;
using CraftoraApi.Infrastructure.Messaging;
using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[EnableRateLimiting("general")]
[Route("api/admin/media-streaming")]
public sealed class AdminMediaStreamingController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IRabbitMqPublisher _publisher;

    public AdminMediaStreamingController(AppDbContext dbContext, IRabbitMqPublisher publisher)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
    }

    [HttpPost("backfill")]
    public async Task<IActionResult> BackfillAsync(
        [FromQuery] int limit = 25,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
        {
            return BadRequest(new { message = "limit 1 ile 100 arasinda olmalidir." });
        }

        var media = await _dbContext.Media
            .AsNoTracking()
            .Where(item =>
                item.IsActive == true &&
                item.Status == MediaStatus.Ready &&
                item.HlsUrl == null &&
                item.VideoUrl != null &&
                item.VideoUrl != string.Empty)
            .OrderBy(item => item.CreatedAt)
            .Take(limit)
            .Select(item => new
            {
                item.Id,
                item.VideoUrl,
                item.ThumbnailUrl
            })
            .ToListAsync(cancellationToken);

        foreach (var item in media)
        {
            await _publisher.PublishProcessVideoCommand(
                new ProcessVideoCommand(
                    item.Id,
                    item.VideoUrl!,
                    Guid.Empty,
                    "Media",
                    string.IsNullOrWhiteSpace(item.ThumbnailUrl)),
                cancellationToken);
        }

        return Accepted(new
        {
            queuedCount = media.Count,
            mediaIds = media.Select(item => item.Id)
        });
    }
}
