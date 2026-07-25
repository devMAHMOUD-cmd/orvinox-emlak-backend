using CraftoraApi.Data;
using CraftoraApi.DTOs.Subscription;
using CraftoraApi.Infrastructure.Messaging;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Elasticsearch;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class SubscriptionService : ISubscriptionService
{
    private const string PublicAssetsBucketName = "public-assets";
    private const string PrivateProductsBucketName = "private-products";
    private const string DefaultPlanCode = "professional";
    private const string PaymentProvider = "stripe_mock";

    private readonly AppDbContext _dbContext;
    private readonly IPaymentService _paymentService;
    private readonly IRabbitMqPublisher _rabbitMqPublisher;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        AppDbContext dbContext,
        IPaymentService paymentService,
        IRabbitMqPublisher rabbitMqPublisher,
        ILogger<SubscriptionService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
        _rabbitMqPublisher = rabbitMqPublisher ?? throw new ArgumentNullException(nameof(rabbitMqPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<SubscriptionPlanResponseDto>> GetPlansAsync()
    {
        return await _dbContext.SellerSubscriptionPlans
            .AsNoTracking()
            .Where(plan => plan.IsActive)
            .OrderBy(plan => plan.SortOrder)
            .ThenBy(plan => plan.MonthlyAmount)
            .Select(plan => new SubscriptionPlanResponseDto(
                plan.Id,
                plan.Code,
                plan.Name,
                plan.Description,
                plan.MonthlyAmount,
                plan.Currency,
                plan.CommissionRate,
                plan.CommissionRate * 100m,
                plan.Features,
                plan.SortOrder))
            .ToListAsync();
    }

    public async Task<SubscriptionResponseDto?> GetMySubscriptionAsync(Guid userId)
    {
        var shop = await GetSellerShopAsync(userId);
        var subscription = await _dbContext.SellerSubscriptions
            .AsNoTracking()
            .Include(item => item.Plan)
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

        var plan = request.PlanId.HasValue
            ? await _dbContext.SellerSubscriptionPlans
                .SingleOrDefaultAsync(item => item.Id == request.PlanId.Value && item.IsActive)
            : await _dbContext.SellerSubscriptionPlans
                .SingleOrDefaultAsync(item => item.Code == DefaultPlanCode && item.IsActive);

        if (plan is null)
        {
            throw new NotFoundException("Abonelik plani bulunamadi.");
        }

        var shop = await GetSellerShopAsync(userId);
        var subscription = await _dbContext.SellerSubscriptions
            .Include(item => item.Plan)
            .FirstOrDefaultAsync(item => item.ShopId == shop.Id);

        if (subscription?.Status == SubStatus.Active &&
            subscription.CurrentPeriodEnd > DateTime.UtcNow)
        {
            throw new ConflictException("Zaten aktif aboneliginiz var.");
        }

        var paymentResult = await _paymentService.ProcessPaymentAsync(
            plan.MonthlyAmount,
            plan.Currency,
            request.CardNumber);

        if (!paymentResult.IsSuccess)
        {
            throw new BadRequestException(paymentResult.ErrorMessage);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        var now = DateTime.UtcNow;

        if (subscription is null)
        {
            subscription = new SellerSubscription
            {
                ShopId = shop.Id,
                CreatedAt = now
            };

            _dbContext.SellerSubscriptions.Add(subscription);
        }

        subscription.ProviderSubscriptionId = $"sub_mock_{Guid.NewGuid():N}";
        subscription.PlanId = plan.Id;
        subscription.Plan = plan;
        subscription.Status = SubStatus.Active;
        subscription.CurrentPeriodEnd = now.AddDays(30);
        subscription.GracePeriodEnd = null;
        subscription.Amount = plan.MonthlyAmount;
        subscription.Currency = plan.Currency;
        subscription.PaymentProvider = PaymentProvider;
        subscription.UpdatedAt = DateTime.UtcNow;

        // Subscription başarılı olunca shop'ı aktif yap
        if (shop.IsActive != true)
        {
            shop.IsActive = true;
            shop.UpdatedAt = DateTime.UtcNow;
            _logger.LogInformation("Shop activated due to subscription. ShopId: {ShopId}", shop.Id);
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(item => item.Id == shop.UserId);
        if (user is null)
        {
            throw new NotFoundException("Kullanici bulunamadi.");
        }

        if (user.Role == UserRole.User)
        {
            user.Role = UserRole.Seller;
        }

        await _dbContext.SaveChangesAsync();
        _dbContext.SellerSubscriptionPayments.Add(new SellerSubscriptionPayment
        {
            SubscriptionId = subscription.Id,
            PlanId = plan.Id,
            ShopId = shop.Id,
            PaymentProvider = PaymentProvider,
            ProviderTransactionId = paymentResult.TransactionId,
            Amount = plan.MonthlyAmount,
            CommissionRate = plan.CommissionRate,
            Currency = plan.Currency,
            Status = "succeeded",
            BillingPeriodStart = now,
            BillingPeriodEnd = subscription.CurrentPeriodEnd,
            CreatedAt = now
        });
        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        await PublishShopIndexMessageAsync(shop.Id);
        await PublishActiveShopContentAsync(shop.Id);

        return MapToResponse(subscription);
    }

    public async Task<SubscriptionResponseDto> CancelSubscriptionAsync(Guid userId)
    {
        var shop = await GetSellerShopAsync(userId);
        var subscription = await _dbContext.SellerSubscriptions
            .Include(item => item.Plan)
            .FirstOrDefaultAsync(item =>
                item.ShopId == shop.Id &&
                item.Status == SubStatus.Active);

        if (subscription is null)
        {
            throw new NotFoundException("Aktif abonelik bulunamadi.");
        }

        subscription.Status = SubStatus.Canceled;
        subscription.UpdatedAt = DateTime.UtcNow;
        shop.IsActive = false;
        shop.UpdatedAt = subscription.UpdatedAt;

        await _dbContext.SaveChangesAsync();
        await PublishShopIndexMessageAsync(shop.Id);
        await PublishInactiveShopContentAsync(shop.Id);

        return MapToResponse(subscription);
    }

    private async Task PublishShopIndexMessageAsync(Guid shopId)
    {
        var shop = await _dbContext.Shops
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == shopId);

        if (shop is null || shop.IsActive != true)
        {
            await _rabbitMqPublisher.PublishShopSyncMessage(new ShopSyncMessage(
                ShopId: shopId,
                Action: "Delete",
                Document: null));
            return;
        }

        await _rabbitMqPublisher.PublishShopSyncMessage(new ShopSyncMessage(
            ShopId: shop.Id,
            Action: "Index",
            Document: new ShopDocument
            {
                Id = shop.Id,
                ShopName = shop.ShopName,
                Slug = shop.Slug,
                ShortDescription = shop.ShortDescription,
                LogoObjectKey = shop.LogoUrl,
                BannerObjectKey = shop.BannerUrl,
                IsActive = true,
                IsVerified = shop.IsVerified == true,
                FollowerCount = shop.FollowerCount ?? 0
            }));
    }

    private async Task PublishInactiveShopContentAsync(Guid shopId)
    {
        var productIds = await _dbContext.Products
            .AsNoTracking()
            .Where(product => product.ShopId == shopId)
            .Select(product => product.Id)
            .ToListAsync();

        foreach (var productId in productIds)
        {
            await _rabbitMqPublisher.PublishProductSyncMessage(new ProductSyncMessage(
                ProductId: productId,
                Action: "Delete",
                Document: null));
        }

        var mediaIds = await _dbContext.Media
            .AsNoTracking()
            .Where(media => media.ShopId == shopId)
            .Select(media => media.Id)
            .ToListAsync();

        foreach (var mediaId in mediaIds)
        {
            await _rabbitMqPublisher.PublishMediaSyncMessage(new MediaSyncMessage(
                MediaId: mediaId,
                Action: "Delete",
                Document: null));
        }
    }

    private async Task PublishActiveShopContentAsync(Guid shopId)
    {
        var products = await _dbContext.Products
            .AsNoTracking()
            .Where(product =>
                product.ShopId == shopId &&
                product.IsActive == true &&
                product.Status == ProductStatus.Published &&
                product.Shop.IsActive == true)
            .Select(product => new ProductDocument
            {
                Id = product.Id,
                Name = product.Title,
                Description = product.Description,
                Price = product.Price,
                CategoryId = product.CategoryId,
                ShopId = product.ShopId,
                ShopName = product.Shop.ShopName,
                IsActive = true,
                IsPublished = true,
                ShopIsActive = true
            })
            .ToListAsync();

        foreach (var product in products)
        {
            await _rabbitMqPublisher.PublishProductSyncMessage(new ProductSyncMessage(
                ProductId: product.Id,
                Action: "Index",
                Document: product));
        }

        var media = await _dbContext.Media
            .AsNoTracking()
            .Include(item => item.Shop)
            .Include(item => item.Product)
            .Where(item =>
                item.ShopId == shopId &&
                item.IsActive == true &&
                item.Shop.IsActive == true)
            .ToListAsync();

        foreach (var item in media)
        {
            await _rabbitMqPublisher.PublishMediaSyncMessage(new MediaSyncMessage(
                MediaId: item.Id,
                Action: "Index",
                Document: new MediaDocument
                {
                    Id = item.Id,
                    Caption = item.Caption,
                    Hashtags = item.Hashtags ?? new List<string>(),
                    ShopId = item.ShopId,
                    ShopName = item.Shop.ShopName,
                    ShopSlug = item.Shop.Slug,
                    ProductId = item.ProductId,
                    ProductTitle = item.Product?.Title,
                    ProductType = item.Product?.Type switch
                    {
                        ProductType.Course => "course",
                        ProductType.DigitalFile => "digital_file",
                        _ => null
                    },
                    ThumbnailObjectKey = ExtractObjectKey(item.ThumbnailUrl, PublicAssetsBucketName),
                    VideoObjectKey = ExtractObjectKey(item.VideoUrl, PrivateProductsBucketName),
                    ProductCoverImageObjectKey = ExtractObjectKey(item.Product?.CoverImageUrl, PublicAssetsBucketName),
                    IsActive = true,
                    ShopIsActive = true,
                    CreatedAt = item.CreatedAt,
                    ViewCount = item.ViewCount ?? 0,
                    LikeCount = item.LikeCount ?? 0,
                    SaveCount = item.SaveCount ?? 0,
                    ShareCount = item.ShareCount ?? 0
                }));
        }
    }

    private static string? ExtractObjectKey(string? value, string bucketName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return value.TrimStart('/');
        }

        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        var bucketPrefix = $"{bucketName}/";
        var bucketIndex = path.IndexOf(bucketPrefix, StringComparison.OrdinalIgnoreCase);
        return bucketIndex >= 0
            ? path[(bucketIndex + bucketPrefix.Length)..]
            : path;
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
            PlanId: subscription.PlanId,
            PlanCode: subscription.Plan.Code,
            PlanName: subscription.Plan.Name,
            CommissionRate: subscription.Plan.CommissionRate,
            CommissionPercent: subscription.Plan.CommissionRate * 100m,
            ProviderSubscriptionId: subscription.ProviderSubscriptionId,
            Status: subscription.Status.ToString(),
            CurrentPeriodEnd: subscription.CurrentPeriodEnd,
            GracePeriodEnd: subscription.GracePeriodEnd,
            Amount: subscription.Amount,
            Currency: subscription.Currency,
            PaymentProvider: subscription.PaymentProvider);
    }
}
