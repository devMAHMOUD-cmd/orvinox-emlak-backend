using CraftoraApi.Data;
using CraftoraApi.DTOs.Notification;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.HostedServices;

public sealed class SubscriptionMonitorWorker : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan GracePeriod = TimeSpan.FromDays(3);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionMonitorWorker> _logger;

    public SubscriptionMonitorWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<SubscriptionMonitorWorker> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
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

        foreach (var subscription in unpaidSubscriptions)
        {
            subscription.Status = SubStatus.Unpaid;
            subscription.UpdatedAt = now;
            subscription.Shop.IsActive = false;
            subscription.Shop.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

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
}
