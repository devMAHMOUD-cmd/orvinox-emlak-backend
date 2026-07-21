using CraftoraApi.Data;
using CraftoraApi.DTOs.Notification;
using CraftoraApi.Infrastructure.Messaging;
using CraftoraApi.Models.Elasticsearch;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.HostedServices;

public sealed class SubscriptionMonitorWorker : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan GracePeriod = TimeSpan.FromDays(3);

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
        var now = DateTime.UtcNow;

        var expiredActiveSubscriptions = await dbContext.SellerSubscriptions
            .Include(subscription => subscription.Shop)
            .Where(subscription =>
                subscription.Status == SubStatus.Active &&
                subscription.CurrentPeriodEnd <= now)
            .ToListAsync(cancellationToken);

        foreach (var subscription in expiredActiveSubscriptions)
        {
            subscription.Status = SubStatus.PastDue;
            subscription.GracePeriodEnd = now.Add(GracePeriod);
            subscription.UpdatedAt = now;
        }

        var unpaidSubscriptions = await dbContext.SellerSubscriptions
            .Include(subscription => subscription.Shop)
            .Where(subscription =>
                subscription.Status == SubStatus.PastDue &&
                subscription.GracePeriodEnd <= now)
            .ToListAsync(cancellationToken);

        var deactivatedShopIds = unpaidSubscriptions
            .Select(subscription => subscription.ShopId)
            .Distinct()
            .ToList();

        foreach (var subscription in unpaidSubscriptions)
        {
            subscription.Status = SubStatus.Unpaid;
            subscription.UpdatedAt = now;
            subscription.Shop.IsActive = false;
            subscription.Shop.UpdatedAt = now;
        }

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        foreach (var shopId in deactivatedShopIds)
        {
            await PublishDeactivationMessagesAsync(shopId, cancellationToken);
        }

        foreach (var subscription in expiredActiveSubscriptions)
        {
            await notificationService.SendNotificationAsync(
                subscription.Shop.UserId,
                "Aboneliğiniz doldu",
                "Dükkanınızın kapanmaması için 3 gün içinde aboneliğinizi yenileyin.",
                NotificationType.System,
                subscription.Id);
        }

        foreach (var subscription in unpaidSubscriptions)
        {
            await notificationService.SendNotificationAsync(
                subscription.Shop.UserId,
                "Mağazanız donduruldu",
                "Ödeme alınamadığı için mağazanız donduruldu.",
                NotificationType.System,
                subscription.Id);
        }
    }

    private async Task PublishDeactivationMessagesAsync(Guid shopId, CancellationToken cancellationToken)
    {
        try
        {
            await _rabbitMqPublisher.PublishShopSyncMessage(new ShopSyncMessage(
                ShopId: shopId,
                Action: "Delete",
                Document: null), cancellationToken);

            var productIds = await GetProductIdsAsync(shopId, cancellationToken);
            foreach (var productId in productIds)
            {
                await _rabbitMqPublisher.PublishProductSyncMessage(new ProductSyncMessage(
                    ProductId: productId,
                    Action: "Delete",
                    Document: null), cancellationToken);
            }

            var mediaIds = await GetMediaIdsAsync(shopId, cancellationToken);
            foreach (var mediaId in mediaIds)
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
                shopId);
        }
    }

    private async Task<List<Guid>> GetProductIdsAsync(Guid shopId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.Products
            .AsNoTracking()
            .Where(product => product.ShopId == shopId)
            .Select(product => product.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<Guid>> GetMediaIdsAsync(Guid shopId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.Media
            .AsNoTracking()
            .Where(media => media.ShopId == shopId)
            .Select(media => media.Id)
            .ToListAsync(cancellationToken);
    }
}
