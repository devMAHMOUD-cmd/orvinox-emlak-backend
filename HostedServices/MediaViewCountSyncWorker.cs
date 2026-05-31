using CraftoraApi.Data;
using CraftoraApi.Redis;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.HostedServices;

public sealed class MediaViewCountSyncWorker : BackgroundService
{
    private const string TrackedViewsSetKey = "media:tracked-views";
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MediaViewCountSyncWorker> _logger;

    public MediaViewCountSyncWorker(
        IServiceProvider serviceProvider,
        ILogger<MediaViewCountSyncWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SyncInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                await SyncViewCountsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Application shutdown.
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Media view count sync worker failed.");
            }
        }
    }

    private async Task SyncViewCountsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var cacheService = scope.ServiceProvider.GetRequiredService<ICacheService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var trackedMediaIds = await cacheService.GetSetMembersAsync(TrackedViewsSetKey);
        if (trackedMediaIds.Count == 0)
        {
            return;
        }

        var syncedMediaIds = new List<string>();

        foreach (var mediaIdValue in trackedMediaIds)
        {
            if (!Guid.TryParse(mediaIdValue, out var mediaId))
            {
                syncedMediaIds.Add(mediaIdValue);
                continue;
            }

            var cachedViewCount = await cacheService.GetAsync<long>(
                GetViewCountCacheKey(mediaId),
                cancellationToken);

            if (cachedViewCount <= 0)
            {
                syncedMediaIds.Add(mediaIdValue);
                continue;
            }

            var media = await dbContext.Media.FirstOrDefaultAsync(
                item => item.Id == mediaId,
                cancellationToken);

            if (media is null)
            {
                syncedMediaIds.Add(mediaIdValue);
                continue;
            }

            var nextViewCount = (media.ViewCount ?? 0) + Math.Min(cachedViewCount, int.MaxValue);
            media.ViewCount = nextViewCount > int.MaxValue
                ? int.MaxValue
                : (int)nextViewCount;
            media.UpdatedAt = DateTime.UtcNow;

            syncedMediaIds.Add(mediaIdValue);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var mediaIdValue in syncedMediaIds)
        {
            await cacheService.RemoveFromSetAsync(TrackedViewsSetKey, mediaIdValue);

            if (Guid.TryParse(mediaIdValue, out var mediaId))
            {
                await cacheService.RemoveAsync(GetViewCountCacheKey(mediaId), cancellationToken);
            }
        }
    }

    private static string GetViewCountCacheKey(Guid mediaId)
    {
        return $"media:viewcount:{mediaId}";
    }
}
