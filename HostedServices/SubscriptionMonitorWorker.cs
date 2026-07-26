using CraftoraApi.Data;
using CraftoraApi.DTOs.Notification;
using CraftoraApi.Infrastructure.Messaging;
using CraftoraApi.Models.Elasticsearch;
using CraftoraApi.Redis;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace CraftoraApi.HostedServices;

public sealed class SubscriptionMonitorWorker : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(10);
    private const string PopularProductsCacheKey = "products:popular:public-active-shops:v4";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRabbitMqPublisher _rabbitMqPublisher;
    private readonly ILogger<SubscriptionMonitorWorker> _logger;

    public SubscriptionMonitorWorker(
        IServiceScopeFactory scopeFactory,
        IRabbitMqPublisher rabbitMqPublisher,
        ILogger<SubscriptionMonitorWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _rabbitMqPublisher = rabbitMqPublisher ?? throw new ArgumentNullException(nameof(rabbitMqPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MonitorSubscriptionsAsync(stoppingToken);
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Application shutdown.
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Seller subscription monitor failed.");
            }
        }
    }

    private async Task MonitorSubscriptionsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var cacheService = scope.ServiceProvider.GetRequiredService<ICacheService>();
        var expiredSubscriptions = await ExpireSubscriptionsAsync(dbContext, cancellationToken);

        foreach (var subscription in expiredSubscriptions)
        {
            await InvalidateVisibilityCachesAsync(
                dbContext,
                cacheService,
                subscription.ShopId,
                cancellationToken);
            await PublishDeactivationMessagesAsync(subscription, cancellationToken);
            await TrySendExpirationNotificationAsync(
                notificationService,
                subscription.UserId,
                "Aboneliğiniz sona erdi",
                "Abonelik dönemi bittiği için mağazanız donduruldu. Yenileyerek tekrar açabilirsiniz.",
                NotificationType.System,
                subscription.SubscriptionId);
        }
    }

    private async Task InvalidateVisibilityCachesAsync(
        AppDbContext dbContext,
        ICacheService cacheService,
        Guid shopId,
        CancellationToken cancellationToken)
    {
        try
        {
            await cacheService.RemoveAsync(PopularProductsCacheKey, cancellationToken);
            var shopSlug = await dbContext.Shops
                .AsNoTracking()
                .Where(shop => shop.Id == shopId)
                .Select(shop => shop.Slug)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(shopSlug))
            {
                await cacheService.RemoveAsync(
                    CacheKeys.PublicShopBySlug(shopSlug),
                    cancellationToken);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Expired subscription caches could not be invalidated. ShopId: {ShopId}",
                shopId);
        }
    }

    private async Task TrySendExpirationNotificationAsync(
        INotificationService notificationService,
        Guid userId,
        string title,
        string message,
        NotificationType type,
        Guid subscriptionId)
    {
        try
        {
            await notificationService.SendNotificationAsync(
                userId,
                title,
                message,
                type,
                subscriptionId);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Subscription expiration notification failed. SubscriptionId: {SubscriptionId}, UserId: {UserId}",
                subscriptionId,
                userId);
        }
    }

    private static async Task<List<ExpiredSubscription>> ExpireSubscriptionsAsync(
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT subscription_id, shop_id, user_id, product_ids, media_ids
            FROM public.expire_seller_subscriptions()
            """;

        var results = new List<ExpiredSubscription>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ExpiredSubscription(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetFieldValue<Guid[]>(3),
                reader.GetFieldValue<Guid[]>(4)));
        }

        return results;
    }

    private async Task PublishDeactivationMessagesAsync(
        ExpiredSubscription subscription,
        CancellationToken cancellationToken)
    {
        try
        {
            await _rabbitMqPublisher.PublishShopSyncMessage(new ShopSyncMessage(
                ShopId: subscription.ShopId,
                Action: "Delete",
                Document: null), cancellationToken);

            foreach (var productId in subscription.ProductIds)
            {
                await _rabbitMqPublisher.PublishProductSyncMessage(new ProductSyncMessage(
                    ProductId: productId,
                    Action: "Delete",
                    Document: null), cancellationToken);
            }

            foreach (var mediaId in subscription.MediaIds)
            {
                await _rabbitMqPublisher.PublishMediaSyncMessage(new MediaSyncMessage(
                    MediaId: mediaId,
                    Action: "Delete",
                    Document: null), cancellationToken);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Elasticsearch deactivation messages could not be published for ShopId: {ShopId}",
                subscription.ShopId);
        }
    }

    private sealed record ExpiredSubscription(
        Guid SubscriptionId,
        Guid ShopId,
        Guid UserId,
        Guid[] ProductIds,
        Guid[] MediaIds);
}
