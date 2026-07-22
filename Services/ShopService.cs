using System.Security.Cryptography;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using CraftoraApi.Data;
using CraftoraApi.DTOs.Shop;
using CraftoraApi.Infrastructure.Messaging;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Elasticsearch;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace CraftoraApi.Services;

public sealed class ShopService : IShopService
{
    private const string PublicAssetsBucketName = "public-assets";
    private const int PublicAssetUrlExpiryMinutes = 60;

    private static readonly DistributedCacheEntryOptions ShopCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
    };

    private readonly AppDbContext _dbContext;
    private readonly ILogger<ShopService> _logger;
    private readonly IDistributedCache _cache;
    private readonly IStorageService _storageService;
    private readonly IRabbitMqPublisher _rabbitMqPublisher;

    public ShopService(
        AppDbContext dbContext,
        ILogger<ShopService> logger,
        IDistributedCache cache,
        IStorageService storageService,
        IRabbitMqPublisher rabbitMqPublisher)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _rabbitMqPublisher = rabbitMqPublisher ?? throw new ArgumentNullException(nameof(rabbitMqPublisher));
    }

    public async Task<ShopResponseDto> CreateShopAsync(Guid userId, CreateShopDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();

        try
        {
            // Keep the identity on the same pooled connection for the whole request.
            // The request middleware resets this setting after the request completes.
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.current_user_id', {userId.ToString("D")}, false);");

            var hasShop = await _dbContext.Shops.AnyAsync(shop => shop.UserId == userId);
            if (hasShop)
            {
                throw new ConflictException("Bu kullaniciya ait bir magaza zaten var.");
            }

            var user = await _dbContext.Users.FirstOrDefaultAsync(item => item.Id == userId);
            if (user is null)
            {
                throw new NotFoundException("Kullanici bulunamadi.");
            }

            var slug = await GenerateUniqueSlugAsync(dto.ShopName);
            var shop = new Shop
            {
                UserId = userId,
                ShopName = dto.ShopName.Trim(),
                Slug = slug,
                ShortDescription = dto.ShortDescription,
                Description = dto.Description,
                ExternalUrl = dto.ExternalUrl,
                SocialLinks = dto.SocialLinks,
                LogoUrl = dto.LogoUrl,
                BannerUrl = dto.BannerUrl,
                FollowerCount = 0,
                Rating = 0,
                IsVerified = false,
                IsActive = false
            };

            _dbContext.Shops.Add(shop);
            await _dbContext.SaveChangesAsync();
            var response = await MapToResponseAsync(shop);
            await transaction.CommitAsync();
            await PublishShopIndexMessageAsync(shop.Id);

            _logger.LogInformation("Shop created. ShopId: {ShopId}, UserId: {UserId}", shop.Id, userId);

            return response;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<ShopResponseDto> GetMyShopAsync(Guid userId)
    {
        var shop = await _dbContext.Shops
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == userId);

        if (shop is null)
        {
            throw new NotFoundException("Magaza bulunamadi.");
        }

        return await MapToResponseAsync(shop);
    }

    public async Task<PublicShopResponseDto> GetShopBySlugAsync(string slug, Guid? currentUserId = null)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new BadRequestException("Magaza slug degeri zorunludur.");
        }

        var normalizedSlug = slug.Trim().ToLowerInvariant();
        var cacheKey = GetShopSlugCacheKey(normalizedSlug);
        var cachedShop = await _cache.GetStringAsync(cacheKey);

        if (!string.IsNullOrWhiteSpace(cachedShop))
        {
            var cachedResponse = JsonSerializer.Deserialize<PublicShopResponseDto>(cachedShop);
            if (cachedResponse is not null)
            {
                return await ApplyCurrentUserFollowStateAsync(cachedResponse, currentUserId);
            }
        }

        var shop = await _dbContext.Shops
            .AsNoTracking()
            .FirstOrDefaultAsync(shop => shop.Slug == normalizedSlug && shop.IsActive == true);

        if (shop is null)
        {
            throw new NotFoundException("Magaza bulunamadi.");
        }

        var response = await MapToPublicResponseAsync(shop);
        await _cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(response),
            ShopCacheOptions);

        return await ApplyCurrentUserFollowStateAsync(response, currentUserId);
    }

    public async Task<PublicShopResponseDto> GetPublicShopByIdAsync(Guid shopId, Guid? currentUserId = null)
    {
        var shop = await _dbContext.Shops
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == shopId && item.IsActive == true);

        if (shop is null)
        {
            throw new NotFoundException("Magaza bulunamadi.");
        }

        var response = await MapToPublicResponseAsync(shop);
        return await ApplyCurrentUserFollowStateAsync(response, currentUserId);
    }

    private async Task DeleteShopHardAsync(Guid id, Guid userId)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();

        try
        {
            var shop = await _dbContext.Shops
                .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);

            if (shop is null)
            {
                throw new NotFoundException("Mağaza", id.ToString());
            }

            // İlişkili subscription'ı sil
            var subscription = await _dbContext.SellerSubscriptions
                .FirstOrDefaultAsync(s => s.ShopId == shop.Id);

            if (subscription is not null)
            {
                _dbContext.SellerSubscriptions.Remove(subscription);
            }

            // Shop'ı sil
            _dbContext.Shops.Remove(shop);

            // User role'ünü geri User'a çevir (artık shop olmadığından)
            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user?.Role == UserRole.Seller)
            {
                // Shop var mı kontrol et
                var hasOtherShop = await _dbContext.Shops
                    .AnyAsync(s => s.UserId == userId);

                if (!hasOtherShop)
                {
                    user.Role = UserRole.User;
                }
            }

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Shop deleted. ShopId: {ShopId}, UserId: {UserId}", shop.Id, userId);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<ShopResponseDto> UpdateShopAsync(Guid userId, UpdateShopDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var shop = await _dbContext.Shops.FirstOrDefaultAsync(shop => shop.UserId == userId);
        if (shop is null)
        {
            throw new NotFoundException("Magaza bulunamadi.");
        }

        var oldSlug = shop.Slug;

        if (!string.IsNullOrWhiteSpace(dto.ShopName) &&
            !string.Equals(shop.ShopName, dto.ShopName.Trim(), StringComparison.Ordinal))
        {
            shop.ShopName = dto.ShopName.Trim();
            shop.Slug = await GenerateUniqueSlugAsync(shop.ShopName, shop.Id);
        }

        if (dto.ShortDescription is not null)
        {
            shop.ShortDescription = dto.ShortDescription;
        }

        if (dto.Description is not null)
        {
            shop.Description = dto.Description;
        }

        if (dto.ExternalUrl is not null)
        {
            shop.ExternalUrl = dto.ExternalUrl;
        }

        if (dto.SocialLinks is not null)
        {
            shop.SocialLinks = dto.SocialLinks;
        }

        if (dto.LogoUrl is not null)
        {
            shop.LogoUrl = dto.LogoUrl;
        }

        if (dto.BannerUrl is not null)
        {
            shop.BannerUrl = dto.BannerUrl;
        }

        await _dbContext.SaveChangesAsync();
        await _cache.RemoveAsync(GetShopSlugCacheKey(oldSlug));

        if (!string.Equals(oldSlug, shop.Slug, StringComparison.Ordinal))
        {
            await _cache.RemoveAsync(GetShopSlugCacheKey(shop.Slug));
        }

        await PublishShopIndexMessageAsync(shop.Id);

        return await MapToResponseAsync(shop);
    }

    public async Task<ShopFollowerListResponseDto> GetMyShopFollowersAsync(
        Guid userId,
        int page = 1,
        int pageSize = 30)
    {
        var shop = await _dbContext.Shops
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == userId);

        if (shop is null)
        {
            throw new NotFoundException("Magaza bulunamadi.");
        }

        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 50);
        var followersQuery = _dbContext.Subscriptions
            .AsNoTracking()
            .Where(subscription =>
                subscription.ShopId == shop.Id &&
                subscription.User.IsActive == true &&
                subscription.User.DeletedAt == null)
            .OrderByDescending(subscription => subscription.CreatedAt);

        var totalCount = await followersQuery.CountAsync();
        var followers = await followersQuery
            .Include(subscription => subscription.User)
                .ThenInclude(user => user.Shop)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();

        var items = followers
            .Select(subscription =>
            {
                var user = subscription.User;
                var followerShop = user.Shop?.IsActive == true ? user.Shop : null;

                return new ShopFollowerDto(
                    UserId: user.Id,
                    FullName: user.FullName,
                    AvatarPublicUrl: GeneratePublicAssetUrl(user.AvatarUrl),
                    ShopId: followerShop?.Id,
                    ShopName: followerShop?.ShopName,
                    ShopSlug: followerShop?.Slug,
                    ShopLogoPublicUrl: GeneratePublicAssetUrl(followerShop?.LogoUrl),
                    IsShopVerified: followerShop?.IsVerified == true,
                    FollowedAt: subscription.CreatedAt);
            })
            .ToList();
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);

        return new ShopFollowerListResponseDto(
            Items: items,
            Page: normalizedPage,
            PageSize: normalizedPageSize,
            TotalCount: totalCount,
            TotalPages: totalPages);
    }

    public async Task<FollowedShopListResponseDto> GetFollowedShopsAsync(
        Guid userId,
        int page = 1,
        int pageSize = 20)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 50);
        var followedShopsQuery = _dbContext.Subscriptions
            .AsNoTracking()
            .Where(subscription =>
                subscription.UserId == userId &&
                subscription.Shop.IsActive == true)
            .OrderByDescending(subscription => subscription.CreatedAt);

        var totalCount = await followedShopsQuery.CountAsync();
        var shops = await followedShopsQuery
            .Select(subscription => subscription.Shop)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();

        if (shops.Count == 0)
        {
            return new FollowedShopListResponseDto(
                Items: Array.Empty<PublicShopResponseDto>(),
                Page: normalizedPage,
                PageSize: normalizedPageSize,
                TotalCount: totalCount,
                TotalPages: 0);
        }

        var shopIds = shops.Select(shop => shop.Id).ToList();
        var productCounts = await _dbContext.Products
            .AsNoTracking()
            .Where(product =>
                shopIds.Contains(product.ShopId) &&
                product.IsActive == true &&
                product.Status == ProductStatus.Published)
            .GroupBy(product => product.ShopId)
            .Select(group => new { ShopId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.ShopId, item => item.Count);
        var followerCounts = await _dbContext.Subscriptions
            .AsNoTracking()
            .Where(subscription => shopIds.Contains(subscription.ShopId))
            .GroupBy(subscription => subscription.ShopId)
            .Select(group => new { ShopId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.ShopId, item => item.Count);

        var items = shops
            .Select(shop => new PublicShopResponseDto(
                Id: shop.Id,
                ShopName: shop.ShopName,
                Slug: shop.Slug,
                ShortDescription: shop.ShortDescription,
                Description: shop.Description,
                LogoUrl: shop.LogoUrl,
                LogoPublicUrl: GeneratePublicAssetUrl(shop.LogoUrl),
                BannerUrl: shop.BannerUrl,
                BannerPublicUrl: GeneratePublicAssetUrl(shop.BannerUrl),
                ExternalUrl: shop.ExternalUrl,
                SocialLinks: shop.SocialLinks,
                FollowerCount: followerCounts.GetValueOrDefault(shop.Id),
                ProductCount: productCounts.GetValueOrDefault(shop.Id),
                Rating: shop.Rating,
                IsVerified: shop.IsVerified == true,
                IsFollowedByCurrentUser: true))
            .ToList();
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);

        return new FollowedShopListResponseDto(
            Items: items,
            Page: normalizedPage,
            PageSize: normalizedPageSize,
            TotalCount: totalCount,
            TotalPages: totalPages);
    }

    public async Task<ShopFollowResponseDto> ToggleFollowAsync(Guid shopId, Guid userId)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            var shop = await _dbContext.Shops
                .FromSqlInterpolated($"SELECT * FROM shops WHERE id = {shopId} AND is_active = true FOR UPDATE")
                .FirstOrDefaultAsync();

            if (shop is null)
            {
                throw new NotFoundException("Magaza bulunamadi.");
            }

            if (shop.UserId == userId)
            {
                throw new BadRequestException("Kendi magazanizi takip edemezsiniz.");
            }

            var subscription = await _dbContext.Subscriptions
                .FirstOrDefaultAsync(subscription => subscription.ShopId == shopId && subscription.UserId == userId);

            var isFollowing = subscription is null;
            if (isFollowing)
            {
                _dbContext.Subscriptions.Add(new Subscription
                {
                    ShopId = shopId,
                    UserId = userId,
                    WantsNotifications = true
                });
            }
            else
            {
                _dbContext.Subscriptions.Remove(subscription!);
            }

            await _dbContext.SaveChangesAsync();
            var followerCount = await _dbContext.Subscriptions
                .AsNoTracking()
                .CountAsync(item => item.ShopId == shopId);

            await transaction.CommitAsync();
            await _cache.RemoveAsync(GetShopSlugCacheKey(shop.Slug));
            await PublishShopIndexMessageAsync(shopId);

            return new ShopFollowResponseDto(shopId, isFollowing, followerCount);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<ShopTrafficReportDto> GetShopTrafficReportAsync(
        Guid shopId,
        Guid userId,
        DateTime startDate,
        DateTime endDate)
    {
        if (startDate > endDate)
        {
            throw new BadRequestException("Baslangic tarihi bitis tarihinden sonra olamaz.");
        }

        await EnsureShopOwnerAsync(shopId, userId);

        var visitsQuery = _dbContext.ShopVisits
            .AsNoTracking()
            .Where(visit =>
                visit.ShopId == shopId &&
                visit.VisitedAt >= startDate &&
                visit.VisitedAt <= endDate);

        var totalVisits = await visitsQuery.CountAsync();

        var uniqueKnownUsers = await visitsQuery
            .Where(visit => visit.UserId.HasValue)
            .Select(visit => visit.UserId)
            .Distinct()
            .CountAsync();

        var uniqueAnonymousIps = await visitsQuery
            .Where(visit => !visit.UserId.HasValue && visit.IpAddress != null)
            .Select(visit => visit.IpAddress)
            .Distinct()
            .CountAsync();

        var dailyVisits = await visitsQuery
            .Where(visit => visit.VisitedAt.HasValue)
            .GroupBy(visit => visit.VisitedAt!.Value.Date)
            .Select(group => new DailyVisitDto(group.Key, group.Count()))
            .OrderBy(visit => visit.Date)
            .ToListAsync();

        return new ShopTrafficReportDto(
            TotalVisits: totalVisits,
            UniqueVisitors: uniqueKnownUsers + uniqueAnonymousIps,
            DailyVisits: dailyVisits);
    }

    public async Task DeleteShopAsync(Guid shopId, Guid userId)
    {
        var shop = await EnsureShopOwnerAsync(shopId, userId);

        shop.IsActive = false;
        await _dbContext.SaveChangesAsync();
        await _cache.RemoveAsync(GetShopSlugCacheKey(shop.Slug));
        await PublishShopIndexMessageAsync(shop.Id);

        _logger.LogInformation("Shop deactivated. ShopId: {ShopId}, UserId: {UserId}", shopId, userId);
    }

    private async Task<string> GenerateUniqueSlugAsync(string shopName)
    {
        return await GenerateUniqueSlugAsync(shopName, excludedShopId: null);
    }

    private async Task PublishShopIndexMessageAsync(Guid shopId)
    {
        var shop = await _dbContext.Shops
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == shopId);

        if (shop is null)
        {
            await _rabbitMqPublisher.PublishShopSyncMessage(new ShopSyncMessage(
                ShopId: shopId,
                Action: "Delete",
                Document: null));
            return;
        }

        if (shop.IsActive != true)
        {
            await _rabbitMqPublisher.PublishShopSyncMessage(new ShopSyncMessage(
                ShopId: shop.Id,
                Action: "Delete",
                Document: null));
            return;
        }

        var followerCount = await _dbContext.Subscriptions
            .AsNoTracking()
            .CountAsync(subscription => subscription.ShopId == shop.Id);

        var document = new ShopDocument
        {
            Id = shop.Id,
            ShopName = shop.ShopName,
            Slug = shop.Slug,
            ShortDescription = shop.ShortDescription,
            LogoObjectKey = shop.LogoUrl,
            BannerObjectKey = shop.BannerUrl,
            IsActive = true,
            IsVerified = shop.IsVerified == true,
            FollowerCount = followerCount
        };

        await _rabbitMqPublisher.PublishShopSyncMessage(new ShopSyncMessage(
            ShopId: shop.Id,
            Action: "Index",
            Document: document));
    }

    private async Task<string> GenerateUniqueSlugAsync(string shopName, Guid? excludedShopId)
    {
        var baseSlug = GenerateSlug(shopName);
        var slug = baseSlug;

        while (await _dbContext.Shops.AnyAsync(shop =>
            shop.Slug == slug && (!excludedShopId.HasValue || shop.Id != excludedShopId.Value)))
        {
            slug = $"{baseSlug}-{RandomNumberGenerator.GetInt32(1000, 9999)}";
        }

        return slug;
    }

    private static string GenerateSlug(string value)
    {
        var slug = value.Trim().ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", string.Empty);
        slug = Regex.Replace(slug, @"\s+", "-");
        slug = Regex.Replace(slug, "-+", "-").Trim('-');

        return string.IsNullOrWhiteSpace(slug)
            ? $"shop-{RandomNumberGenerator.GetInt32(1000, 9999)}"
            : slug;
    }

    private async Task<Shop> EnsureShopOwnerAsync(Guid shopId, Guid userId)
    {
        var shop = await _dbContext.Shops.FirstOrDefaultAsync(shop => shop.Id == shopId);
        if (shop is null || shop.IsActive != true)
        {
            throw new NotFoundException("Magaza bulunamadi.");
        }

        if (shop.UserId != userId)
        {
            throw new ForbiddenException("Bu magaza uzerinde islem yapma yetkiniz yok.");
        }

        return shop;
    }

    private static string GetShopSlugCacheKey(string slug)
    {
        return $"shop:public:slug:v2:{slug.Trim().ToLowerInvariant()}";
    }

    private async Task<PublicShopResponseDto> MapToPublicResponseAsync(Shop shop)
    {
        var productCount = await _dbContext.Products
            .AsNoTracking()
            .CountAsync(product =>
                product.ShopId == shop.Id &&
                product.IsActive == true &&
                product.Status == ProductStatus.Published);

        var followerCount = await _dbContext.Subscriptions
            .AsNoTracking()
            .CountAsync(subscription => subscription.ShopId == shop.Id);

        return new PublicShopResponseDto(
            Id: shop.Id,
            ShopName: shop.ShopName,
            Slug: shop.Slug,
            ShortDescription: shop.ShortDescription,
            Description: shop.Description,
            LogoUrl: shop.LogoUrl,
            LogoPublicUrl: GeneratePublicAssetUrl(shop.LogoUrl),
            BannerUrl: shop.BannerUrl,
            BannerPublicUrl: GeneratePublicAssetUrl(shop.BannerUrl),
            ExternalUrl: shop.ExternalUrl,
            SocialLinks: shop.SocialLinks,
            FollowerCount: followerCount,
            ProductCount: productCount,
            Rating: shop.Rating,
            IsVerified: shop.IsVerified == true);
    }

    private async Task<PublicShopResponseDto> ApplyCurrentUserFollowStateAsync(
        PublicShopResponseDto response,
        Guid? currentUserId)
    {
        if (!currentUserId.HasValue)
        {
            return response with { IsFollowedByCurrentUser = false };
        }

        var isFollowing = await _dbContext.Subscriptions
            .AsNoTracking()
            .AnyAsync(subscription =>
                subscription.ShopId == response.Id &&
                subscription.UserId == currentUserId.Value);

        return response with { IsFollowedByCurrentUser = isFollowing };
    }

    private async Task<ShopResponseDto> MapToResponseAsync(Shop shop)
    {
        var hasActiveSubscription = await HasActiveSubscriptionAsync(shop.Id);
        var followingCount = await _dbContext.Subscriptions
            .AsNoTracking()
            .CountAsync(subscription =>
                subscription.UserId == shop.UserId &&
                subscription.Shop.IsActive == true);

        return new ShopResponseDto(
            Id: shop.Id,
            UserId: shop.UserId,
            ShopName: shop.ShopName,
            Slug: shop.Slug,
            ShortDescription: shop.ShortDescription,
            Description: shop.Description,
            LogoUrl: shop.LogoUrl,
            BannerUrl: shop.BannerUrl,
            FollowerCount: shop.FollowerCount,
            Rating: shop.Rating,
            IsVerified: shop.IsVerified,
            IsActive: shop.IsActive,
            HasActiveSubscription: hasActiveSubscription,
            CreatedAt: shop.CreatedAt,
            FollowingCount: followingCount);
    }

    private string? GeneratePublicAssetUrl(string? objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return null;
        }

        return _storageService.GeneratePresignedDownloadUrl(
            PublicAssetsBucketName,
            objectKey,
            PublicAssetUrlExpiryMinutes);
    }

    private async Task<bool> HasActiveSubscriptionAsync(Guid shopId)
    {
        return await _dbContext.SellerSubscriptions
            .AsNoTracking()
            .AnyAsync(subscription =>
                subscription.ShopId == shopId &&
                subscription.Status == SubStatus.Active &&
                subscription.CurrentPeriodEnd > DateTime.UtcNow);
    }
}
