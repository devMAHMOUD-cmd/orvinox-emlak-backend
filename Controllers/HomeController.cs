using CraftoraApi.Data;
using CraftoraApi.DTOs.Home;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Discovery;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CraftoraApi.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/home")]
public sealed class HomeController : ControllerBase
{
    private const string PublicAssetsBucketName = "public-assets";
    private const string PrivateProductsBucketName = "private-products";
    private const int PublicUrlExpiryMinutes = 60;

    private readonly AppDbContext _dbContext;
    private readonly IStorageService _storageService;
    private readonly IDiscoveryTrackingTokenService _discoveryTrackingTokenService;
    private readonly IDiscoveryRankingService _discoveryRankingService;

    public HomeController(
        AppDbContext dbContext,
        IStorageService storageService,
        IDiscoveryTrackingTokenService discoveryTrackingTokenService,
        IDiscoveryRankingService discoveryRankingService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _discoveryTrackingTokenService = discoveryTrackingTokenService
            ?? throw new ArgumentNullException(nameof(discoveryTrackingTokenService));
        _discoveryRankingService = discoveryRankingService
            ?? throw new ArgumentNullException(nameof(discoveryRankingService));
    }

    [HttpGet("trending-products")]
    public async Task<IActionResult> GetTrendingProductsAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 30);
        var since = DateTime.UtcNow.AddDays(-7);

        var products = await _dbContext.Products
            .AsNoTracking()
            .Include(item => item.Shop)
            .Where(item =>
                item.Type == ProductType.DigitalFile &&
                item.IsActive == true &&
                item.Status == ProductStatus.Published &&
                item.Shop.IsActive == true)
            .Select(item => new
            {
                Product = item,
                RecentViews = _dbContext.AnalyticsEvents.Count(evt =>
                    evt.ProductId == item.Id &&
                    evt.EventType == AnalyticsEventType.ProductView &&
                    evt.CreatedAt >= since),
                RecentCartAdds = _dbContext.AnalyticsEvents.Count(evt =>
                    evt.ProductId == item.Id &&
                    evt.EventType == AnalyticsEventType.AddToCart &&
                    evt.CreatedAt >= since),
                RecentPurchases = _dbContext.Orders.Count(order =>
                    order.ProductId == item.Id &&
                    order.Status == OrderStatus.Completed &&
                    order.CreatedAt >= since),
                FirstImageObjectKey = item.ProductImages
                    .OrderBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .Select(image => image.ObjectKey)
                    .FirstOrDefault()
            })
            .OrderByDescending(item =>
                item.RecentPurchases * 12 +
                item.RecentCartAdds * 4 +
                item.RecentViews)
            .ThenByDescending(item => item.RecentPurchases)
            .ThenByDescending(item => item.RecentCartAdds)
            .ThenByDescending(item => item.RecentViews)
            .ThenByDescending(item => item.Product.SalesCount ?? 0)
            .ThenByDescending(item => item.Product.RatingAverage ?? 0)
            .ThenByDescending(item => item.Product.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        var feedSessionId = Guid.NewGuid();
        var startPosition = (normalizedPage - 1) * normalizedPageSize;
        return Ok(products.Select((item, index) => new HomeTrendingProductDto(
            Id: item.Product.Id,
            Title: item.Product.Title,
            Description: item.Product.Description ?? string.Empty,
            Price: item.Product.Price,
            OriginalPrice: item.Product.OriginalPrice,
            Currency: item.Product.Currency ?? "USD",
            CoverImagePublicUrl: GeneratePublicAssetUrl(
                item.Product.CoverImageUrl ?? item.FirstImageObjectKey),
            RatingAverage: item.Product.RatingAverage,
            ReviewCount: item.Product.ReviewCount ?? 0,
            SalesCount: item.Product.SalesCount ?? 0,
            ViewCount: item.RecentViews,
            ShopId: item.Product.ShopId,
            ShopName: item.Product.Shop.ShopName,
            ShopSlug: item.Product.Shop.Slug,
            TrackingToken: _discoveryTrackingTokenService.Issue(
                null,
                "product",
                item.Product.Id,
                item.Product.ShopId,
                feedSessionId,
                startPosition + index),
            RecentCartCount: item.RecentCartAdds,
            RecentPurchaseCount: item.RecentPurchases,
            TrendScore: item.RecentPurchases * 12 + item.RecentCartAdds * 4 + item.RecentViews)));
    }

    [HttpGet("trending-shops")]
    public async Task<IActionResult> GetTrendingShopsAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 30);
        var since = DateTime.UtcNow.AddDays(-7);

        var shops = await _dbContext.Shops
            .AsNoTracking()
            .Where(item => item.IsActive == true)
            .Select(item => new
            {
                Shop = item,
                FollowerCount = _dbContext.Subscriptions.Count(subscription => subscription.ShopId == item.Id),
                RecentVisits = _dbContext.ShopVisits.Count(visit =>
                    visit.ShopId == item.Id &&
                    visit.VisitedAt >= since),
                RecentFollowers = _dbContext.Subscriptions.Count(subscription =>
                    subscription.ShopId == item.Id &&
                    subscription.CreatedAt >= since),
                RecentProductViews = _dbContext.AnalyticsEvents.Count(evt =>
                    evt.ShopId == item.Id &&
                    evt.EventType == AnalyticsEventType.ProductView &&
                    evt.CreatedAt >= since),
                RecentPurchases = _dbContext.Orders.Count(order =>
                    order.ShopId == item.Id &&
                    order.Status == OrderStatus.Completed &&
                    order.CreatedAt >= since)
            })
            .OrderByDescending(item =>
                item.RecentPurchases * 12 +
                item.RecentFollowers * 6 +
                item.RecentProductViews * 2 +
                item.RecentVisits)
            .ThenByDescending(item => item.RecentPurchases)
            .ThenByDescending(item => item.RecentFollowers)
            .ThenByDescending(item => item.RecentVisits)
            .ThenByDescending(item => item.FollowerCount)
            .ThenByDescending(item => item.Shop.Rating ?? 0)
            .ThenByDescending(item => item.Shop.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        var currentUserId = GetOptionalCurrentUserId();
        var followedShopIds = currentUserId.HasValue && shops.Count > 0
            ? await _dbContext.Subscriptions
                .AsNoTracking()
                .Where(subscription =>
                    subscription.UserId == currentUserId.Value &&
                    shops.Select(item => item.Shop.Id).Contains(subscription.ShopId))
                .Select(subscription => subscription.ShopId)
                .ToHashSetAsync(cancellationToken)
            : new HashSet<Guid>();

        return Ok(shops.Select(item => new HomeTrendingShopDto(
            Id: item.Shop.Id,
            ShopName: item.Shop.ShopName,
            Slug: item.Shop.Slug,
            ShortDescription: item.Shop.ShortDescription,
            LogoPublicUrl: GeneratePublicAssetUrl(item.Shop.LogoUrl),
            BannerPublicUrl: GeneratePublicAssetUrl(item.Shop.BannerUrl),
            FollowerCount: item.FollowerCount,
            Rating: item.Shop.Rating,
            VisitCount: item.RecentVisits,
            IsVerified: item.Shop.IsVerified == true,
            IsFollowedByCurrentUser: followedShopIds.Contains(item.Shop.Id),
            RecentFollowerCount: item.RecentFollowers,
            RecentPurchaseCount: item.RecentPurchases,
            RecentProductViewCount: item.RecentProductViews,
            TrendScore: item.RecentPurchases * 12 +
                item.RecentFollowers * 6 +
                item.RecentProductViews * 2 +
                item.RecentVisits)));
    }

    [HttpGet("featured-courses")]
    public async Task<IActionResult> GetFeaturedCoursesAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 30);
        var currentUserId = GetOptionalCurrentUserId();
        if (currentUserId.HasValue)
        {
            return Ok(await GetPersonalizedCoursesAsync(
                currentUserId.Value,
                normalizedPage,
                normalizedPageSize,
                cancellationToken));
        }

        var courses = await _dbContext.Courses
            .AsNoTracking()
            .Include(item => item.Product)
                .ThenInclude(product => product.Shop)
            .Include(item => item.Product)
                .ThenInclude(product => product.ProductImages)
            .Include(item => item.CourseSections)
                .ThenInclude(section => section.CourseLessons)
            .Where(item =>
                item.Product.IsActive == true &&
                item.Product.Status == ProductStatus.Published &&
                item.Product.Shop.IsActive == true)
            .OrderByDescending(item => item.Product.IsFeatured == true)
            .ThenByDescending(item => item.Product.SalesCount ?? 0)
            .ThenByDescending(item => item.Product.RatingAverage ?? 0)
            .ThenByDescending(item => item.Product.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        var feedSessionId = Guid.NewGuid();
        var startPosition = (normalizedPage - 1) * normalizedPageSize;
        return Ok(courses.Select((course, index) =>
        {
            var activeSections = course.CourseSections.Where(section => section.IsActive).ToList();
            var activeLessons = activeSections
                .SelectMany(section => section.CourseLessons)
                .Where(lesson => lesson.IsActive)
                .ToList();

            return new HomeFeaturedCourseDto(
                CourseId: course.Id,
                ProductId: course.ProductId,
                Title: course.Product.Title,
                Description: course.Product.Description ?? string.Empty,
                Price: course.Product.Price,
                OriginalPrice: course.Product.OriginalPrice,
                Currency: course.Product.Currency ?? "USD",
                CoverImagePublicUrl: GeneratePublicAssetUrl(GetProductCoverObjectKey(course.Product)),
                Level: course.Level,
                TotalDurationInMinutes: course.TotalDurationInMinutes,
                LessonCount: activeLessons.Count,
                SectionCount: activeSections.Count,
                RatingAverage: course.Product.RatingAverage,
                ReviewCount: course.Product.ReviewCount ?? 0,
                SalesCount: course.Product.SalesCount ?? 0,
                ShopId: course.Product.ShopId,
                ShopName: course.Product.Shop.ShopName,
                ShopSlug: course.Product.Shop.Slug,
                ShopLogoPublicUrl: GeneratePublicAssetUrl(course.Product.Shop.LogoUrl),
                TrackingToken: _discoveryTrackingTokenService.Issue(
                    null,
                    "course",
                    course.Id,
                    course.Product.ShopId,
                    feedSessionId,
                    startPosition + index));
        }));
    }

    [HttpGet("reels")]
    public async Task<IActionResult> GetHomeReelsAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 30);

        var media = await _dbContext.Media
            .AsNoTracking()
            .Include(item => item.Shop)
            .Include(item => item.Product)
                .ThenInclude(product => product!.ProductImages)
            .Where(DiscoveryEligibility.ReadyMedia)
            .OrderByDescending(item => item.ViewCount ?? 0)
            .ThenByDescending(item => item.LikeCount ?? 0)
            .ThenByDescending(item => item.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        var currentUserId = GetOptionalCurrentUserId();
        var feedSessionId = Guid.NewGuid();
        var startPosition = (normalizedPage - 1) * normalizedPageSize;

        return Ok(media.Select((item, index) => new HomeReelDto(
            Id: item.Id,
            ShopId: item.ShopId,
            ShopName: item.Shop.ShopName,
            ShopLogoPublicUrl: GeneratePublicAssetUrl(item.Shop.LogoUrl),
            ProductId: item.ProductId,
            ProductTitle: item.Product?.Title,
            VideoUrl: item.VideoUrl,
            VideoPublicUrl: GeneratePrivateProductUrl(item.VideoUrl),
            ThumbnailPublicUrl: GeneratePublicAssetUrl(item.ThumbnailUrl)
                ?? GeneratePublicAssetUrl(GetProductCoverObjectKey(item.Product)),
            Caption: item.Caption,
            ViewCount: item.ViewCount ?? 0,
            LikeCount: item.LikeCount ?? 0,
            SaveCount: item.SaveCount ?? 0,
            ShareCount: item.ShareCount ?? 0,
            CommentCount: item.CommentCount ?? 0,
            Hashtags: item.Hashtags ?? new List<string>(),
            CreatedAt: item.CreatedAt,
            TrackingToken: _discoveryTrackingTokenService.Issue(
                currentUserId,
                "media",
                item.Id,
                item.ShopId,
                feedSessionId,
                startPosition + index))));
    }

    private async Task<List<HomeTrendingProductDto>> GetPersonalizedProductsAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var rankedIds = await _discoveryRankingService.GetPersonalizedProductIdsAsync(
            userId,
            "product",
            cancellationToken);
        var pageIds = rankedIds.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        if (pageIds.Count == 0)
        {
            return [];
        }

        var products = await _dbContext.Products
            .AsNoTracking()
            .Include(item => item.Shop)
            .Include(item => item.ProductImages)
            .Where(item => pageIds.Contains(item.Id))
            .ToListAsync(cancellationToken);
        var productsById = products.ToDictionary(item => item.Id);
        var since = DateTime.UtcNow.AddDays(-7);
        var viewCounts = await _dbContext.AnalyticsEvents
            .AsNoTracking()
            .Where(item =>
                item.ProductId.HasValue &&
                pageIds.Contains(item.ProductId.Value) &&
                item.EventType == AnalyticsEventType.ProductView &&
                item.CreatedAt >= since)
            .GroupBy(item => item.ProductId!.Value)
            .Select(group => new { ProductId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.ProductId, item => item.Count, cancellationToken);
        var feedSessionId = Guid.NewGuid();
        var startPosition = (page - 1) * pageSize;

        return pageIds
            .Where(productsById.ContainsKey)
            .Select((productId, index) =>
            {
                var product = productsById[productId];
                return new HomeTrendingProductDto(
                    Id: product.Id,
                    Title: product.Title,
                    Description: product.Description ?? string.Empty,
                    Price: product.Price,
                    OriginalPrice: product.OriginalPrice,
                    Currency: product.Currency ?? "USD",
                    CoverImagePublicUrl: GeneratePublicAssetUrl(GetProductCoverObjectKey(product)),
                    RatingAverage: product.RatingAverage,
                    ReviewCount: product.ReviewCount ?? 0,
                    SalesCount: product.SalesCount ?? 0,
                    ViewCount: viewCounts.GetValueOrDefault(product.Id),
                    ShopId: product.ShopId,
                    ShopName: product.Shop.ShopName,
                    ShopSlug: product.Shop.Slug,
                    TrackingToken: _discoveryTrackingTokenService.Issue(
                        userId,
                        "product",
                        product.Id,
                        product.ShopId,
                        feedSessionId,
                        startPosition + index));
            })
            .ToList();
    }

    private async Task<List<HomeFeaturedCourseDto>> GetPersonalizedCoursesAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var rankedIds = await _discoveryRankingService.GetPersonalizedProductIdsAsync(
            userId,
            "course",
            cancellationToken);
        var pageIds = rankedIds.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        if (pageIds.Count == 0)
        {
            return [];
        }

        var courses = await _dbContext.Courses
            .AsNoTracking()
            .Include(item => item.Product)
                .ThenInclude(product => product.Shop)
            .Include(item => item.Product)
                .ThenInclude(product => product.ProductImages)
            .Include(item => item.CourseSections)
                .ThenInclude(section => section.CourseLessons)
            .Where(item => pageIds.Contains(item.Id))
            .ToListAsync(cancellationToken);
        var coursesById = courses.ToDictionary(item => item.Id);
        var feedSessionId = Guid.NewGuid();
        var startPosition = (page - 1) * pageSize;

        return pageIds
            .Where(coursesById.ContainsKey)
            .Select((courseId, index) =>
            {
                var course = coursesById[courseId];
                var activeSections = course.CourseSections
                    .Where(section => section.IsActive)
                    .ToList();
                var activeLessons = activeSections
                    .SelectMany(section => section.CourseLessons)
                    .Count(lesson => lesson.IsActive);

                return new HomeFeaturedCourseDto(
                    CourseId: course.Id,
                    ProductId: course.ProductId,
                    Title: course.Product.Title,
                    Description: course.Product.Description ?? string.Empty,
                    Price: course.Product.Price,
                    OriginalPrice: course.Product.OriginalPrice,
                    Currency: course.Product.Currency ?? "USD",
                    CoverImagePublicUrl: GeneratePublicAssetUrl(GetProductCoverObjectKey(course.Product)),
                    Level: course.Level,
                    TotalDurationInMinutes: course.TotalDurationInMinutes,
                    LessonCount: activeLessons,
                    SectionCount: activeSections.Count,
                    RatingAverage: course.Product.RatingAverage,
                    ReviewCount: course.Product.ReviewCount ?? 0,
                    SalesCount: course.Product.SalesCount ?? 0,
                    ShopId: course.Product.ShopId,
                    ShopName: course.Product.Shop.ShopName,
                    ShopSlug: course.Product.Shop.Slug,
                    ShopLogoPublicUrl: GeneratePublicAssetUrl(course.Product.Shop.LogoUrl),
                    TrackingToken: _discoveryTrackingTokenService.Issue(
                        userId,
                        "course",
                        course.Id,
                        course.Product.ShopId,
                        feedSessionId,
                        startPosition + index));
            })
            .ToList();
    }

    private Guid? GetOptionalCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    private static string? GetProductCoverObjectKey(Models.Entities.Product? product)
    {
        return product?.CoverImageUrl
            ?? product?.ProductImages
                .OrderBy(image => image.SortOrder)
                .ThenBy(image => image.CreatedAt)
                .Select(image => image.ObjectKey)
                .FirstOrDefault();
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
            PublicUrlExpiryMinutes);
    }

    private string? GeneratePrivateProductUrl(string? objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return null;
        }

        return _storageService.GeneratePresignedDownloadUrl(
            PrivateProductsBucketName,
            objectKey,
            PublicUrlExpiryMinutes);
    }
}
