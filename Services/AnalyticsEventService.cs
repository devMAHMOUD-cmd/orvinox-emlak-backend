using System.Net;
using System.Text.Json;
using CraftoraApi.Data;
using CraftoraApi.DTOs.Analytics;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class AnalyticsEventService : IAnalyticsEventService
{
    private const int MaxMetadataLength = 8192;

    private readonly AppDbContext _dbContext;
    private readonly ILogger<AnalyticsEventService> _logger;

    public AnalyticsEventService(
        AppDbContext dbContext,
        ILogger<AnalyticsEventService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AnalyticsEventResponseDto> TrackAsync(
        TrackAnalyticsEventDto dto,
        Guid? userId,
        IPAddress? ipAddress,
        string? userAgent,
        string? fallbackReferrer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var eventType = ParseEventType(dto.EventType);
        ValidatePayload(eventType, dto.ProductId, dto.MediaId, dto.OrderId, dto.ShopId);
        var metadata = SerializeMetadata(dto.Metadata);
        if (metadata.Length > MaxMetadataLength)
        {
            throw new BadRequestException("Analytics metadata en fazla 8192 karakter olabilir.");
        }

        var analyticsEvent = await CreateEventAsync(
            eventType,
            dto.ProductId,
            dto.MediaId,
            dto.OrderId,
            dto.ShopId,
            userId,
            dto.SessionId,
            dto.Source,
            dto.Referrer ?? fallbackReferrer,
            dto.UtmSource,
            dto.UtmMedium,
            dto.UtmCampaign,
            dto.DeviceType,
            ipAddress,
            userAgent,
            metadata,
            cancellationToken);

        if (analyticsEvent is null)
        {
            return new AnalyticsEventResponseDto(
                Id: Guid.Empty,
                ShopId: Guid.Empty,
                ProductId: dto.ProductId,
                MediaId: dto.MediaId,
                UserId: userId,
                OrderId: dto.OrderId,
                EventType: ToWireName(eventType),
                CreatedAt: null);
        }

        return MapToResponse(analyticsEvent);
    }

    public Task TrackAddToCartAsync(Guid productId, Guid userId, CancellationToken cancellationToken = default)
    {
        return TrackInternalAsync(AnalyticsEventType.AddToCart, productId, null, null, null, userId, cancellationToken);
    }

    public Task TrackCheckoutStartedAsync(Guid productId, Guid userId, CancellationToken cancellationToken = default)
    {
        return TrackInternalAsync(AnalyticsEventType.CheckoutStarted, productId, null, null, null, userId, cancellationToken);
    }

    public Task TrackPurchaseCompletedAsync(Guid orderId, Guid userId, CancellationToken cancellationToken = default)
    {
        return TrackInternalAsync(AnalyticsEventType.PurchaseCompleted, null, null, orderId, null, userId, cancellationToken);
    }

    public Task TrackDownloadClickedAsync(Guid productId, Guid userId, CancellationToken cancellationToken = default)
    {
        return TrackInternalAsync(AnalyticsEventType.DownloadClicked, productId, null, null, null, userId, cancellationToken);
    }

    public async Task TrackMediaViewAsync(
        Guid mediaId,
        Guid? userId,
        IPAddress? ipAddress,
        string? userAgent,
        string? referrer,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await CreateEventAsync(
                AnalyticsEventType.MediaView,
                productId: null,
                mediaId,
                orderId: null,
                shopId: null,
                userId,
                sessionId: null,
                source: "media_view",
                referrer,
                utmSource: null,
                utmMedium: null,
                utmCampaign: null,
                deviceType: null,
                ipAddress,
                userAgent,
                metadata: "{}",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Media view analytics event kaydedilemedi. MediaId: {MediaId}, UserId: {UserId}",
                mediaId,
                userId);
        }
    }

    private async Task TrackInternalAsync(
        AnalyticsEventType eventType,
        Guid? productId,
        Guid? mediaId,
        Guid? orderId,
        Guid? shopId,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        try
        {
            await CreateEventAsync(
                eventType,
                productId,
                mediaId,
                orderId,
                shopId,
                userId,
                sessionId: null,
                source: "backend",
                referrer: null,
                utmSource: null,
                utmMedium: null,
                utmCampaign: null,
                deviceType: null,
                ipAddress: null,
                userAgent: null,
                metadata: "{}",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Analytics event kaydedilemedi. EventType: {EventType}, ProductId: {ProductId}, OrderId: {OrderId}, UserId: {UserId}",
                eventType,
                productId,
                orderId,
                userId);
        }
    }

    private async Task<AnalyticsEvent?> CreateEventAsync(
        AnalyticsEventType eventType,
        Guid? productId,
        Guid? mediaId,
        Guid? orderId,
        Guid? shopId,
        Guid? userId,
        string? sessionId,
        string? source,
        string? referrer,
        string? utmSource,
        string? utmMedium,
        string? utmCampaign,
        string? deviceType,
        IPAddress? ipAddress,
        string? userAgent,
        string? metadata,
        CancellationToken cancellationToken)
    {
        var resolvedShopId = await ResolveShopIdAsync(productId, mediaId, orderId, shopId, cancellationToken);
        if (userId.HasValue && await IsShopOwnerAsync(resolvedShopId, userId.Value, cancellationToken))
        {
            return null;
        }

        var analyticsEvent = new AnalyticsEvent
        {
            Id = Guid.NewGuid(),
            ShopId = resolvedShopId,
            ProductId = productId,
            MediaId = mediaId,
            OrderId = orderId,
            UserId = userId,
            EventType = eventType,
            SessionId = TrimToLength(sessionId, 100),
            Source = TrimToLength(source, 100),
            Referrer = referrer,
            UtmSource = TrimToLength(utmSource, 100),
            UtmMedium = TrimToLength(utmMedium, 100),
            UtmCampaign = TrimToLength(utmCampaign, 150),
            DeviceType = TrimToLength(deviceType, 30),
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Metadata = string.IsNullOrWhiteSpace(metadata) ? "{}" : metadata,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.AnalyticsEvents.Add(analyticsEvent);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return analyticsEvent;
    }

    private async Task<Guid> ResolveShopIdAsync(
        Guid? productId,
        Guid? mediaId,
        Guid? orderId,
        Guid? shopId,
        CancellationToken cancellationToken)
    {
        if (productId.HasValue)
        {
            var productShopId = await _dbContext.Products
                .AsNoTracking()
                .Where(product => product.Id == productId.Value)
                .Select(product => (Guid?)product.ShopId)
                .FirstOrDefaultAsync(cancellationToken);

            if (!productShopId.HasValue)
            {
                throw new NotFoundException("Urun bulunamadi.");
            }

            return productShopId.Value;
        }

        if (mediaId.HasValue)
        {
            var mediaShopId = await _dbContext.Media
                .AsNoTracking()
                .Where(media => media.Id == mediaId.Value)
                .Select(media => (Guid?)media.ShopId)
                .FirstOrDefaultAsync(cancellationToken);

            if (!mediaShopId.HasValue)
            {
                throw new NotFoundException("Medya bulunamadi.");
            }

            return mediaShopId.Value;
        }

        if (orderId.HasValue)
        {
            var orderShopId = await _dbContext.Orders
                .AsNoTracking()
                .Where(order => order.Id == orderId.Value)
                .Select(order => (Guid?)order.ShopId)
                .FirstOrDefaultAsync(cancellationToken);

            if (!orderShopId.HasValue)
            {
                throw new NotFoundException("Siparis bulunamadi.");
            }

            return orderShopId.Value;
        }

        if (shopId.HasValue)
        {
            var shopExists = await _dbContext.Shops
                .AsNoTracking()
                .AnyAsync(shop => shop.Id == shopId.Value, cancellationToken);

            if (!shopExists)
            {
                throw new NotFoundException("Magaza bulunamadi.");
            }

            return shopId.Value;
        }

        throw new BadRequestException("Analytics kaydi icin shopId, productId veya orderId gereklidir.");
    }

    private async Task<bool> IsShopOwnerAsync(Guid shopId, Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.Shops
            .AsNoTracking()
            .AnyAsync(shop => shop.Id == shopId && shop.UserId == userId, cancellationToken);
    }

    private static void ValidatePayload(
        AnalyticsEventType eventType,
        Guid? productId,
        Guid? mediaId,
        Guid? orderId,
        Guid? shopId)
    {
        if ((eventType is AnalyticsEventType.ProductView or AnalyticsEventType.AddToCart or AnalyticsEventType.DownloadClicked) &&
            !productId.HasValue)
        {
            throw new BadRequestException($"{ToWireName(eventType)} olayi icin productId zorunludur.");
        }

        if (eventType == AnalyticsEventType.MediaView && !mediaId.HasValue)
        {
            throw new BadRequestException("media_view olayi icin mediaId zorunludur.");
        }

        if (eventType == AnalyticsEventType.PurchaseCompleted && !orderId.HasValue)
        {
            throw new BadRequestException("purchase_completed olayi icin orderId zorunludur.");
        }

        if (eventType == AnalyticsEventType.ShopVisit && !shopId.HasValue && !productId.HasValue && !mediaId.HasValue && !orderId.HasValue)
        {
            throw new BadRequestException("shop_visit olayi icin shopId zorunludur.");
        }
    }

    private static AnalyticsEventType ParseEventType(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new BadRequestException("Analytics eventType zorunludur.");
        }

        return eventType.Trim().ToLowerInvariant() switch
        {
            "shop_visit" or "shopvisit" => AnalyticsEventType.ShopVisit,
            "product_view" or "productview" => AnalyticsEventType.ProductView,
            "media_view" or "mediaview" => AnalyticsEventType.MediaView,
            "add_to_cart" or "addtocart" => AnalyticsEventType.AddToCart,
            "checkout_started" or "checkoutstarted" => AnalyticsEventType.CheckoutStarted,
            "purchase_completed" or "purchasecompleted" => AnalyticsEventType.PurchaseCompleted,
            "download_clicked" or "downloadclicked" => AnalyticsEventType.DownloadClicked,
            _ => throw new BadRequestException("Gecersiz analytics eventType degeri.")
        };
    }

    private static string SerializeMetadata(Dictionary<string, JsonElement>? metadata)
    {
        return metadata is null || metadata.Count == 0
            ? "{}"
            : JsonSerializer.Serialize(metadata);
    }

    private static AnalyticsEventResponseDto MapToResponse(AnalyticsEvent analyticsEvent)
    {
        return new AnalyticsEventResponseDto(
            Id: analyticsEvent.Id,
            ShopId: analyticsEvent.ShopId,
            ProductId: analyticsEvent.ProductId,
            MediaId: analyticsEvent.MediaId,
            UserId: analyticsEvent.UserId,
            OrderId: analyticsEvent.OrderId,
            EventType: ToWireName(analyticsEvent.EventType),
            CreatedAt: analyticsEvent.CreatedAt);
    }

    private static string ToWireName(AnalyticsEventType eventType)
    {
        return eventType switch
        {
            AnalyticsEventType.ShopVisit => "shop_visit",
            AnalyticsEventType.ProductView => "product_view",
            AnalyticsEventType.MediaView => "media_view",
            AnalyticsEventType.AddToCart => "add_to_cart",
            AnalyticsEventType.CheckoutStarted => "checkout_started",
            AnalyticsEventType.PurchaseCompleted => "purchase_completed",
            AnalyticsEventType.DownloadClicked => "download_clicked",
            _ => eventType.ToString()
        };
    }

    private static string? TrimToLength(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmedValue = value.Trim();
        return trimmedValue.Length <= maxLength
            ? trimmedValue
            : trimmedValue[..maxLength];
    }
}
