using System.Data;
using System.Data.Common;
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

            await IncrementViewCountAsync(
                dbContext,
                mediaId,
                cachedViewCount,
                cancellationToken);
            syncedMediaIds.Add(mediaIdValue);
        }

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

    private static async Task<bool> IncrementViewCountAsync(
        AppDbContext dbContext,
        Guid mediaId,
        long increment,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT public.increment_media_view_count(
                    CAST(@media_id AS uuid),
                    CAST(@increment AS bigint))
                """;

            AddParameter(command, "media_id", mediaId);
            AddParameter(command, "increment", increment);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is true;
        }
        finally
        {
            if (openedHere)
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
