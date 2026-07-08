using CraftoraApi.Data;
using CraftoraApi.DTOs.Course;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class PublicCourseService : IPublicCourseService
{
    private const string PublicAssetsBucketName = "public-assets";
    private const int PublicMediaUrlExpiryMinutes = 60;

    private readonly AppDbContext _dbContext;
    private readonly IStorageService _storageService;

    public PublicCourseService(
        AppDbContext dbContext,
        IStorageService storageService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
    }

    public Task<PublicCourseListResponseDto> GetFeaturedCoursesAsync(
        Guid? currentUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        return GetCoursesAsync(
            currentUserId,
            query: null,
            categoryId: null,
            level: null,
            minPrice: null,
            maxPrice: null,
            sort: "featured",
            page,
            pageSize,
            cancellationToken);
    }

    public async Task<PublicCourseListResponseDto> GetCoursesAsync(
        Guid? currentUserId,
        string? query,
        Guid? categoryId,
        string? level,
        decimal? minPrice,
        decimal? maxPrice,
        string? sort,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 50);
        var coursesQuery = BuildPublicCourseQuery();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{query.Trim()}%";
            coursesQuery = coursesQuery.Where(course =>
                EF.Functions.ILike(course.Product.Title, pattern) ||
                (course.Product.Description != null && EF.Functions.ILike(course.Product.Description, pattern)) ||
                EF.Functions.ILike(course.Product.Shop.ShopName, pattern) ||
                EF.Functions.ILike(course.Product.Category.Name, pattern) ||
                EF.Functions.ILike(course.Product.Category.Slug, pattern) ||
                course.Product.Tags.Any(tag => EF.Functions.ILike(tag, pattern)) ||
                course.CourseSections.Any(section =>
                    EF.Functions.ILike(section.Title, pattern) ||
                    section.CourseLessons.Any(lesson => EF.Functions.ILike(lesson.Title, pattern))));
        }

        if (categoryId.HasValue)
        {
            coursesQuery = coursesQuery.Where(course => course.Product.CategoryId == categoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(level))
        {
            var normalizedLevel = level.Trim();
            coursesQuery = coursesQuery.Where(course => course.Level == normalizedLevel);
        }

        if (minPrice.HasValue)
        {
            coursesQuery = coursesQuery.Where(course => course.Product.Price >= minPrice.Value);
        }

        if (maxPrice.HasValue)
        {
            coursesQuery = coursesQuery.Where(course => course.Product.Price <= maxPrice.Value);
        }

        coursesQuery = ApplySorting(coursesQuery, sort);

        var totalCount = await coursesQuery.CountAsync(cancellationToken);
        var courses = await coursesQuery
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);
        var purchasedProductIds = await GetPurchasedProductIdsAsync(currentUserId, courses, cancellationToken);
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);

        return new PublicCourseListResponseDto(
            Items: courses.Select(course => MapToListItem(course, purchasedProductIds.Contains(course.ProductId))).ToList(),
            Page: normalizedPage,
            PageSize: normalizedPageSize,
            TotalCount: totalCount,
            TotalPages: totalPages);
    }

    public async Task<PublicCourseDetailDto> GetPublicCourseDetailAsync(
        Guid? currentUserId,
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        var course = await BuildPublicCourseQuery()
            .FirstOrDefaultAsync(course => course.Id == courseId, cancellationToken);

        if (course is null)
        {
            throw new NotFoundException("Kurs bulunamadi.");
        }

        var isPurchased = currentUserId.HasValue &&
            await _dbContext.UserLibraries
                .AsNoTracking()
                .AnyAsync(
                    item => item.UserId == currentUserId.Value && item.ProductId == course.ProductId,
                    cancellationToken);

        return MapToDetail(course, isPurchased);
    }

    private IQueryable<Course> BuildPublicCourseQuery()
    {
        return _dbContext.Courses
            .AsNoTracking()
            .Include(course => course.Product)
                .ThenInclude(product => product.Shop)
            .Include(course => course.Product)
                .ThenInclude(product => product.Category)
            .Include(course => course.CourseSections)
                .ThenInclude(section => section.CourseLessons)
                    .ThenInclude(lesson => lesson.LessonResources)
            .Where(course =>
                course.Product.Type == ProductType.Course &&
                course.Product.IsActive == true &&
                course.Product.Status == ProductStatus.Published &&
                course.Product.Shop.IsActive == true)
            .AsSplitQuery();
    }

    private static IQueryable<Course> ApplySorting(IQueryable<Course> query, string? sort)
    {
        return sort?.Trim().ToLowerInvariant() switch
        {
            "popular" => query
                .OrderByDescending(course => course.Product.SalesCount ?? 0)
                .ThenByDescending(course => course.Product.RatingAverage ?? 0)
                .ThenByDescending(course => course.Product.CreatedAt),
            "newest" => query
                .OrderByDescending(course => course.Product.CreatedAt),
            "featured" or _ => query
                .OrderByDescending(course => course.Product.IsFeatured == true)
                .ThenByDescending(course => course.Product.SalesCount ?? 0)
                .ThenByDescending(course => course.Product.RatingAverage ?? 0)
                .ThenByDescending(course => course.Product.CreatedAt)
        };
    }

    private async Task<HashSet<Guid>> GetPurchasedProductIdsAsync(
        Guid? currentUserId,
        IReadOnlyCollection<Course> courses,
        CancellationToken cancellationToken)
    {
        if (!currentUserId.HasValue || courses.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var productIds = courses.Select(course => course.ProductId).ToList();

        return await _dbContext.UserLibraries
            .AsNoTracking()
            .Where(item => item.UserId == currentUserId.Value && productIds.Contains(item.ProductId))
            .Select(item => item.ProductId)
            .ToHashSetAsync(cancellationToken);
    }

    private PublicCourseListItemDto MapToListItem(Course course, bool isPurchased)
    {
        var activeSections = course.CourseSections.Where(section => section.IsActive).ToList();
        var activeLessons = activeSections
            .SelectMany(section => section.CourseLessons)
            .Where(lesson => lesson.IsActive)
            .ToList();

        return new PublicCourseListItemDto(
            CourseId: course.Id,
            ProductId: course.ProductId,
            Title: course.Product.Title,
            Description: course.Product.Description ?? string.Empty,
            CoverImagePublicUrl: GeneratePublicAssetUrl(course.Product.CoverImageUrl),
            Price: course.Product.Price,
            OriginalPrice: course.Product.OriginalPrice,
            Currency: course.Product.Currency ?? "USD",
            Level: course.Level,
            TotalDurationInMinutes: course.TotalDurationInMinutes,
            LessonCount: activeLessons.Count,
            SectionCount: activeSections.Count,
            RatingAverage: course.Product.RatingAverage,
            ReviewCount: course.Product.ReviewCount ?? 0,
            SalesCount: course.Product.SalesCount ?? 0,
            IsPurchased: isPurchased,
            ShopId: course.Product.ShopId,
            ShopName: course.Product.Shop.ShopName,
            ShopSlug: course.Product.Shop.Slug,
            ShopLogoPublicUrl: GeneratePublicAssetUrl(course.Product.Shop.LogoUrl));
    }

    private PublicCourseDetailDto MapToDetail(Course course, bool isPurchased)
    {
        var activeSections = course.CourseSections
            .Where(section => section.IsActive)
            .OrderBy(section => section.SortOrder)
            .ToList();
        var activeLessons = activeSections
            .SelectMany(section => section.CourseLessons)
            .Where(lesson => lesson.IsActive)
            .ToList();

        return new PublicCourseDetailDto(
            CourseId: course.Id,
            ProductId: course.ProductId,
            Title: course.Product.Title,
            Description: course.Product.Description ?? string.Empty,
            CoverImagePublicUrl: GeneratePublicAssetUrl(course.Product.CoverImageUrl),
            PreviewVideoPublicUrl: GeneratePublicAssetUrl(course.Product.PreviewVideoUrl),
            Price: course.Product.Price,
            OriginalPrice: course.Product.OriginalPrice,
            Currency: course.Product.Currency ?? "USD",
            Level: course.Level,
            TotalDurationInMinutes: course.TotalDurationInMinutes,
            SectionCount: activeSections.Count,
            LessonCount: activeLessons.Count,
            IsPurchased: isPurchased,
            Shop: new PublicCourseShopDto(
                Id: course.Product.ShopId,
                ShopName: course.Product.Shop.ShopName,
                Slug: course.Product.Shop.Slug,
                LogoPublicUrl: GeneratePublicAssetUrl(course.Product.Shop.LogoUrl),
                ShortDescription: course.Product.Shop.ShortDescription),
            Sections: activeSections
                .Select(section => new PublicCourseSectionDto(
                    Id: section.Id,
                    Title: section.Title,
                    SortOrder: section.SortOrder,
                    Lessons: section.CourseLessons
                        .Where(lesson => lesson.IsActive)
                        .OrderBy(lesson => lesson.SortOrder)
                        .Select(lesson => MapLesson(lesson, isPurchased))
                        .ToList()))
                .ToList());
    }

    private PublicCourseLessonDto MapLesson(CourseLesson lesson, bool isPurchased)
    {
        var isLocked = !isPurchased && !lesson.IsFreePreview;

        return new PublicCourseLessonDto(
            Id: lesson.Id,
            Title: lesson.Title,
            DurationInSeconds: lesson.DurationInSeconds,
            SortOrder: lesson.SortOrder,
            IsFreePreview: lesson.IsFreePreview,
            IsLocked: isLocked,
            Resources: lesson.LessonResources
                .OrderBy(resource => resource.Title)
                .Select(resource => new PublicCourseResourceDto(
                    Id: resource.Id,
                    Title: resource.Title,
                    FileName: GetFileName(resource.FileUrl),
                    ResourceType: resource.ResourceType,
                    IsLocked: !isPurchased))
                .ToList());
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
            PublicMediaUrlExpiryMinutes);
    }

    private static string? GetFileName(string? objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return null;
        }

        var normalizedObjectKey = objectKey.Trim();
        var separatorIndex = normalizedObjectKey.LastIndexOf('/');

        return separatorIndex >= 0 && separatorIndex < normalizedObjectKey.Length - 1
            ? normalizedObjectKey[(separatorIndex + 1)..]
            : normalizedObjectKey;
    }
}
