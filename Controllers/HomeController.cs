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

    public HomeController(AppDbContext dbContext, IStorageService storageService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
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
                    evt.CreatedAt >= since)
            })
            .OrderByDescending(item => item.RecentViews)
            .ThenByDescending(item => item.Product.SalesCount ?? 0)
            .ThenByDescending(item => item.Product.RatingAverage ?? 0)
            .ThenByDescending(item => item.Product.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return Ok(products.Select(item => new HomeTrendingProductDto(
            Id: item.Product.Id,
            Title: item.Product.Title,
            Description: item.Product.Description ?? string.Empty,
            Price: item.Product.Price,
            OriginalPrice: item.Product.OriginalPrice,
            Currency: item.Product.Currency ?? "USD",
            CoverImagePublicUrl: GeneratePublicAssetUrl(item.Product.CoverImageUrl),
            RatingAverage: item.Product.RatingAverage,
            ReviewCount: item.Product.ReviewCount ?? 0,
            SalesCount: item.Product.SalesCount ?? 0,
            ViewCount: item.RecentViews,
            ShopId: item.Product.ShopId,
            ShopName: item.Product.Shop.ShopName,
            ShopSlug: item.Product.Shop.Slug)));
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
                    visit.VisitedAt >= since)
            })
            .OrderByDescending(item => item.RecentVisits)
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
            IsFollowedByCurrentUser: followedShopIds.Contains(item.Shop.Id))));
    }

    [HttpGet("featured-courses")]
    public async Task<IActionResult> GetFeaturedCoursesAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 30);

        var courses = await _dbContext.Courses
            .AsNoTracking()
            .Include(item => item.Product)
                .ThenInclude(product => product.Shop)
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

        return Ok(courses.Select(course =>
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
                CoverImagePublicUrl: GeneratePublicAssetUrl(course.Product.CoverImageUrl),
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
                ShopLogoPublicUrl: GeneratePublicAssetUrl(course.Product.Shop.LogoUrl));
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
            .Where(DiscoveryEligibility.ReadyMedia)
            .OrderByDescending(item => item.ViewCount ?? 0)
            .ThenByDescending(item => item.LikeCount ?? 0)
            .ThenByDescending(item => item.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return Ok(media.Select(item => new HomeReelDto(
            Id: item.Id,
            ShopId: item.ShopId,
            ShopName: item.Shop.ShopName,
            ShopLogoPublicUrl: GeneratePublicAssetUrl(item.Shop.LogoUrl),
            ProductId: item.ProductId,
            ProductTitle: item.Product?.Title,
            VideoUrl: item.VideoUrl,
            VideoPublicUrl: GeneratePrivateProductUrl(item.VideoUrl),
            ThumbnailPublicUrl: GeneratePublicAssetUrl(item.ThumbnailUrl) ?? GeneratePublicAssetUrl(item.Product?.CoverImageUrl),
            Caption: item.Caption,
            ViewCount: item.ViewCount ?? 0,
            LikeCount: item.LikeCount ?? 0,
            SaveCount: item.SaveCount ?? 0,
            ShareCount: item.ShareCount ?? 0,
            CommentCount: item.CommentCount ?? 0,
            Hashtags: item.Hashtags ?? new List<string>(),
            CreatedAt: item.CreatedAt)));
    }

    private Guid? GetOptionalCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
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
