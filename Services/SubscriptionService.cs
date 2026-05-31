using CraftoraApi.Data;
using CraftoraApi.DTOs.Subscription;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class SubscriptionService : ISubscriptionService
{
    private const decimal MonthlyAmount = 25.00m;
    private const string Currency = "USD";
    private const string PaymentProvider = "stripe_mock";

    private readonly AppDbContext _dbContext;
    private readonly IPaymentService _paymentService;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        AppDbContext dbContext,
        IPaymentService paymentService,
        ILogger<SubscriptionService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SubscriptionResponseDto?> GetMySubscriptionAsync(Guid userId)
    {
        var shop = await GetSellerShopAsync(userId);
        var subscription = await _dbContext.SellerSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.ShopId == shop.Id);

        return subscription is null
            ? null
            : MapToResponse(subscription);
    }

    public async Task<SubscriptionResponseDto> StartSubscriptionAsync(
        Guid userId,
        StartSubscriptionRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var shop = await GetSellerShopAsync(userId);
        var subscription = await _dbContext.SellerSubscriptions
            .FirstOrDefaultAsync(item => item.ShopId == shop.Id);

        if (subscription?.Status == SubStatus.Active)
        {
            throw new ConflictException("Zaten aktif aboneliginiz var.");
        }

        var paymentResult = await _paymentService.ProcessPaymentAsync(
            MonthlyAmount,
            Currency,
            request.CardNumber);

        if (!paymentResult.IsSuccess)
        {
            throw new BadRequestException(paymentResult.ErrorMessage);
        }

        if (subscription is null)
        {
            subscription = new SellerSubscription
            {
                ShopId = shop.Id,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.SellerSubscriptions.Add(subscription);
        }

        subscription.ProviderSubscriptionId = $"sub_mock_{Guid.NewGuid():N}";
        subscription.Status = SubStatus.Active;
        subscription.CurrentPeriodEnd = DateTime.UtcNow.AddDays(30);
        subscription.GracePeriodEnd = null;
        subscription.Amount = MonthlyAmount;
        subscription.Currency = Currency;
        subscription.PaymentProvider = PaymentProvider;
        subscription.UpdatedAt = DateTime.UtcNow;

        // Subscription başarılı olunca shop'ı aktif yap
        if (shop.IsActive != true)
        {
            shop.IsActive = true;
            shop.UpdatedAt = DateTime.UtcNow;
            _logger.LogInformation("Shop activated due to subscription. ShopId: {ShopId}", shop.Id);
        }

        await _dbContext.SaveChangesAsync();

        return MapToResponse(subscription);
    }

    public async Task<SubscriptionResponseDto> CancelSubscriptionAsync(Guid userId)
    {
        var shop = await GetSellerShopAsync(userId);
        var subscription = await _dbContext.SellerSubscriptions
            .FirstOrDefaultAsync(item =>
                item.ShopId == shop.Id &&
                item.Status == SubStatus.Active);

        if (subscription is null)
        {
            throw new NotFoundException("Aktif abonelik bulunamadi.");
        }

        subscription.Status = SubStatus.Canceled;
        subscription.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return MapToResponse(subscription);
    }

    private async Task<Shop> GetSellerShopAsync(Guid userId)
    {
        var shop = await _dbContext.Shops.FirstOrDefaultAsync(item =>
            item.UserId == userId);

        if (shop is null)
        {
            throw new BadRequestException("Abonelik islemleri icin bir magazaniz olmalidir.");
        }

        return shop;
    }

    private static SubscriptionResponseDto MapToResponse(SellerSubscription subscription)
    {
        return new SubscriptionResponseDto(
            Id: subscription.Id,
            ShopId: subscription.ShopId,
            ProviderSubscriptionId: subscription.ProviderSubscriptionId,
            Status: subscription.Status.ToString(),
            CurrentPeriodEnd: subscription.CurrentPeriodEnd,
            GracePeriodEnd: subscription.GracePeriodEnd,
            Amount: subscription.Amount,
            Currency: subscription.Currency,
            PaymentProvider: subscription.PaymentProvider);
    }
}
