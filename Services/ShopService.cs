using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CraftoraApi.Data;
using CraftoraApi.DTOs.Shop;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace CraftoraApi.Services;

public sealed class ShopService : IShopService
{
    private static readonly DistributedCacheEntryOptions ShopCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
    };

    private readonly AppDbContext _dbContext;
    private readonly ILogger<ShopService> _logger;
    private readonly IDistributedCache _cache;

    public ShopService(
        AppDbContext dbContext,
        ILogger<ShopService> logger,
        IDistributedCache cache)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task<ShopResponseDto> CreateShopAsync(Guid userId, CreateShopDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();

        try
        {
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

            if (user.Role == UserRole.User)
            {
                user.Role = UserRole.Seller;
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

    public async Task<ShopResponseDto> GetShopBySlugAsync(string slug)
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
            var cachedResponse = JsonSerializer.Deserialize<ShopResponseDto>(cachedShop);
            if (cachedResponse is not null)
            {
                return cachedResponse;
            }
        }

        var shop = await _dbContext.Shops
            .AsNoTracking()
            .FirstOrDefaultAsync(shop => shop.Slug == normalizedSlug && shop.IsActive == true);

        if (shop is null)
        {
            throw new NotFoundException("Magaza bulunamadi.");
        }

        var response = await MapToResponseAsync(shop);
        await _cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(response),
            ShopCacheOptions);

        return response;
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

        return await MapToResponseAsync(shop);
    }

    public async Task ToggleFollowAsync(Guid shopId, Guid userId)
    {
        var shop = await _dbContext.Shops.FirstOrDefaultAsync(shop => shop.Id == shopId && shop.IsActive == true);
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

        if (subscription is null)
        {
            _dbContext.Subscriptions.Add(new Subscription
            {
                ShopId = shopId,
                UserId = userId,
                WantsNotifications = true
            });

            shop.FollowerCount = (shop.FollowerCount ?? 0) + 1;
        }
        else
        {
            _dbContext.Subscriptions.Remove(subscription);
            shop.FollowerCount = Math.Max((shop.FollowerCount ?? 0) - 1, 0);
        }

        await _dbContext.SaveChangesAsync();
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

        _logger.LogInformation("Shop deactivated. ShopId: {ShopId}, UserId: {UserId}", shopId, userId);
    }

    private async Task<string> GenerateUniqueSlugAsync(string shopName)
    {
        return await GenerateUniqueSlugAsync(shopName, excludedShopId: null);
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
        return $"shop:slug:{slug.Trim().ToLowerInvariant()}";
    }

    private async Task<ShopResponseDto> MapToResponseAsync(Shop shop)
    {
        var hasActiveSubscription = await HasActiveSubscriptionAsync(shop.Id);

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
            CreatedAt: shop.CreatedAt);
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
