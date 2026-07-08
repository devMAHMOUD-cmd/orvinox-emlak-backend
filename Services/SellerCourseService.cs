using CraftoraApi.Data;
using CraftoraApi.DTOs.Course;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class SellerCourseService : ISellerCourseService
{
    private const string PublicAssetsBucketName = "public-assets";
    private const int PublicMediaUrlExpiryMinutes = 60;

    private static readonly HashSet<string> AllowedLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Beginner",
        "Intermediate",
        "Advanced"
    };

    private static readonly HashSet<string> AllowedResourceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Document",
        "SourceCode",
        "ExternalLink"
    };

    private readonly AppDbContext _dbContext;
    private readonly IStorageService _storageService;
    private readonly IGamificationService _gamificationService;

    public SellerCourseService(
        AppDbContext dbContext,
        IStorageService storageService,
        IGamificationService gamificationService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _gamificationService = gamificationService ?? throw new ArgumentNullException(nameof(gamificationService));
    }

    public async Task<SellerCourseListResponseDto> GetSellerCoursesAsync(
        Guid userId,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var shop = await GetSellerShopAsync(userId, cancellationToken);
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var parsedStatus = ParseProductStatus(status);

        var query = _dbContext.Courses
            .AsNoTracking()
            .Include(course => course.Product)
                .ThenInclude(product => product.ProductImages)
            .Include(course => course.CourseSections)
                .ThenInclude(section => section.CourseLessons)
            .Where(course =>
                course.Product.ShopId == shop.Id &&
                course.Product.IsActive == true);

        if (parsedStatus.HasValue)
        {
            query = query.Where(course => course.Product.Status == parsedStatus.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var courses = await query
            .OrderByDescending(course => course.Product.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);
        var productIds = courses.Select(course => course.ProductId).ToList();
        var enrolledCounts = await GetEnrolledCountsAsync(productIds, cancellationToken);
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);

        return new SellerCourseListResponseDto(
            Items: courses.Select(course => MapToListItem(course, enrolledCounts.GetValueOrDefault(course.ProductId))).ToList(),
            Page: normalizedPage,
            PageSize: normalizedPageSize,
            TotalCount: totalCount,
            TotalPages: totalPages);
    }

    public async Task<SellerCourseDetailDto> GetSellerCourseDetailAsync(
        Guid userId,
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        var course = await GetOwnedCourseTreeAsync(userId, courseId, asTracking: false, cancellationToken);
        var enrolledCount = await _dbContext.UserLibraries
            .AsNoTracking()
            .CountAsync(item => item.ProductId == course.ProductId, cancellationToken);

        return MapToDetail(course, enrolledCount, includeInactive: true);
    }

    public async Task<SellerCourseDetailDto> CreateSellerCourseAsync(
        Guid userId,
        CreateSellerCourseDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ValidateLevel(dto.Level);
        ValidateResources(dto.Sections.SelectMany(section => section.Lessons).SelectMany(lesson => lesson.Resources));

        var shop = await GetSellerShopAsync(userId, cancellationToken);
        var categoryId = await ResolveCategoryIdAsync(dto.CategoryId, cancellationToken);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var product = new Product
        {
            Id = Guid.NewGuid(),
            ShopId = shop.Id,
            CategoryId = categoryId,
            Type = ProductType.Course,
            Title = dto.Title.Trim(),
            Description = dto.Description,
            Price = dto.Price,
            OriginalPrice = dto.OriginalPrice,
            Currency = "USD",
            CoverImageUrl = dto.CoverImageUrl,
            PreviewVideoUrl = dto.PreviewVideoUrl,
            Metadata = dto.Metadata,
            Status = dto.Status,
            Tags = NormalizeTags(dto.Tags),
            RatingAverage = 0,
            ReviewCount = 0,
            SalesCount = 0,
            IsActive = true,
            IsFeatured = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var course = new Course
        {
            Id = Guid.NewGuid(),
            Product = product,
            ProductId = product.Id,
            Level = NormalizeLevel(dto.Level),
            TotalDurationInMinutes = dto.TotalDurationInMinutes,
            IsCertificateIncluded = dto.IsCertificateIncluded,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        AddSections(course, dto.Sections);

        _dbContext.Courses.Add(course);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (product.Status == ProductStatus.Published)
        {
            await _gamificationService.AwardPointsAsync(
                shop.UserId,
                "create_product",
                10.0m,
                product.Id,
                preventDuplicate: true,
                cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);

        return await GetSellerCourseDetailAsync(userId, course.Id, cancellationToken);
    }

    public async Task<SellerCourseDetailDto> UpdateSellerCourseAsync(
        Guid userId,
        Guid courseId,
        UpdateSellerCourseDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ValidateLevel(dto.Level);

        var course = await GetOwnedCourseTreeAsync(userId, courseId, asTracking: true, cancellationToken);
        var categoryId = await ResolveCategoryIdAsync(dto.CategoryId, cancellationToken);
        var previousStatus = course.Product.Status;

        course.Product.CategoryId = categoryId;
        course.Product.Title = dto.Title.Trim();
        course.Product.Description = dto.Description;
        course.Product.Price = dto.Price;
        course.Product.OriginalPrice = dto.OriginalPrice;
        course.Product.CoverImageUrl = dto.CoverImageUrl;
        course.Product.PreviewVideoUrl = dto.PreviewVideoUrl;
        course.Product.Metadata = dto.Metadata;
        course.Product.Status = dto.Status;
        course.Product.Tags = NormalizeTags(dto.Tags);
        course.Product.Type = ProductType.Course;
        course.Product.UpdatedAt = DateTime.UtcNow;

        course.Level = NormalizeLevel(dto.Level);
        course.TotalDurationInMinutes = dto.TotalDurationInMinutes;
        course.IsCertificateIncluded = dto.IsCertificateIncluded;
        course.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (previousStatus != ProductStatus.Published && course.Product.Status == ProductStatus.Published)
        {
            await _gamificationService.AwardPointsAsync(
                userId,
                "create_product",
                10.0m,
                course.ProductId,
                preventDuplicate: true,
                cancellationToken);
        }

        return await GetSellerCourseDetailAsync(userId, course.Id, cancellationToken);
    }

    public async Task ArchiveSellerCourseAsync(
        Guid userId,
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        var course = await GetOwnedCourseTreeAsync(userId, courseId, asTracking: true, cancellationToken);

        course.Product.Status = ProductStatus.Archived;
        course.Product.UpdatedAt = DateTime.UtcNow;
        course.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<SellerCourseSectionDto> CreateSectionAsync(
        Guid userId,
        Guid courseId,
        CreateSellerCourseSectionDto dto,
        CancellationToken cancellationToken = default)
    {
        var course = await GetOwnedCourseTreeAsync(userId, courseId, asTracking: true, cancellationToken);
        var section = new CourseSection
        {
            CourseId = course.Id,
            Title = dto.Title.Trim(),
            SortOrder = dto.SortOrder,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var lessonDto in dto.Lessons)
        {
            section.CourseLessons.Add(CreateLessonEntity(lessonDto));
        }

        _dbContext.CourseSections.Add(section);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapSection(section, includeInactive: true);
    }

    public async Task<SellerCourseSectionDto> UpdateSectionAsync(
        Guid userId,
        Guid sectionId,
        UpdateSellerCourseSectionDto dto,
        CancellationToken cancellationToken = default)
    {
        var section = await GetOwnedSectionAsync(userId, sectionId, asTracking: true, cancellationToken);

        section.Title = dto.Title.Trim();
        section.SortOrder = dto.SortOrder;
        section.IsActive = dto.IsActive;
        section.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapSection(section, includeInactive: true);
    }

    public async Task DeleteSectionAsync(
        Guid userId,
        Guid sectionId,
        CancellationToken cancellationToken = default)
    {
        var section = await GetOwnedSectionAsync(userId, sectionId, asTracking: true, cancellationToken);

        section.IsActive = false;
        section.UpdatedAt = DateTime.UtcNow;

        foreach (var lesson in section.CourseLessons)
        {
            lesson.IsActive = false;
            lesson.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<SellerCourseLessonDto> CreateLessonAsync(
        Guid userId,
        Guid sectionId,
        CreateSellerCourseLessonDto dto,
        CancellationToken cancellationToken = default)
    {
        _ = await GetOwnedSectionAsync(userId, sectionId, asTracking: false, cancellationToken);
        var lesson = CreateLessonEntity(dto);
        lesson.CourseSectionId = sectionId;

        _dbContext.CourseLessons.Add(lesson);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapLesson(lesson);
    }

    public async Task<SellerCourseLessonDto> UpdateLessonAsync(
        Guid userId,
        Guid lessonId,
        UpdateSellerCourseLessonDto dto,
        CancellationToken cancellationToken = default)
    {
        var lesson = await GetOwnedLessonAsync(userId, lessonId, asTracking: true, cancellationToken);

        lesson.Title = dto.Title.Trim();
        lesson.VideoUrl = dto.VideoUrl;
        lesson.DurationInSeconds = dto.DurationInSeconds;
        lesson.SortOrder = dto.SortOrder;
        lesson.IsFreePreview = dto.IsFreePreview;
        lesson.IsActive = dto.IsActive;
        lesson.UpdatedAt = DateTime.UtcNow;

        _dbContext.LessonResources.RemoveRange(lesson.LessonResources);
        lesson.LessonResources.Clear();
        foreach (var resourceDto in dto.Resources)
        {
            lesson.LessonResources.Add(CreateResourceEntity(resourceDto));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapLesson(lesson);
    }

    public async Task DeleteLessonAsync(
        Guid userId,
        Guid lessonId,
        CancellationToken cancellationToken = default)
    {
        var lesson = await GetOwnedLessonAsync(userId, lessonId, asTracking: true, cancellationToken);

        lesson.IsActive = false;
        lesson.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Course> GetOwnedCourseTreeAsync(
        Guid userId,
        Guid courseId,
        bool asTracking,
        CancellationToken cancellationToken)
    {
        var query = BuildCourseTreeQuery(asTracking)
            .Where(course => course.Id == courseId);

        var course = await query.FirstOrDefaultAsync(cancellationToken);
        if (course is null)
        {
            throw new NotFoundException("Kurs bulunamadi.");
        }

        if (course.Product.Shop.UserId != userId)
        {
            throw new ForbiddenException("Bu kursu yonetme yetkiniz yok.");
        }

        return course;
    }

    private async Task<CourseSection> GetOwnedSectionAsync(
        Guid userId,
        Guid sectionId,
        bool asTracking,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.CourseSections
            .Include(section => section.Course)
                .ThenInclude(course => course.Product)
                    .ThenInclude(product => product.Shop)
            .Include(section => section.CourseLessons)
                .ThenInclude(lesson => lesson.LessonResources)
            .Include(section => section.CourseQuizzes)
            .Where(section => section.Id == sectionId);

        if (!asTracking)
        {
            query = query.AsNoTracking();
        }

        var section = await query.FirstOrDefaultAsync(cancellationToken);
        if (section is null)
        {
            throw new NotFoundException("Kurs bolumu bulunamadi.");
        }

        if (section.Course.Product.Shop.UserId != userId)
        {
            throw new ForbiddenException("Bu bolumu yonetme yetkiniz yok.");
        }

        return section;
    }

    private async Task<CourseLesson> GetOwnedLessonAsync(
        Guid userId,
        Guid lessonId,
        bool asTracking,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.CourseLessons
            .Include(lesson => lesson.CourseSection)
                .ThenInclude(section => section.Course)
                    .ThenInclude(course => course.Product)
                        .ThenInclude(product => product.Shop)
            .Include(lesson => lesson.LessonResources)
            .Where(lesson => lesson.Id == lessonId);

        if (!asTracking)
        {
            query = query.AsNoTracking();
        }

        var lesson = await query.FirstOrDefaultAsync(cancellationToken);
        if (lesson is null)
        {
            throw new NotFoundException("Ders bulunamadi.");
        }

        if (lesson.CourseSection.Course.Product.Shop.UserId != userId)
        {
            throw new ForbiddenException("Bu dersi yonetme yetkiniz yok.");
        }

        return lesson;
    }

    private IQueryable<Course> BuildCourseTreeQuery(bool asTracking)
    {
        IQueryable<Course> query = _dbContext.Courses
            .Include(course => course.Product)
                .ThenInclude(product => product.Shop)
            .Include(course => course.Product)
                .ThenInclude(product => product.ProductImages)
            .Include(course => course.CourseSections)
                .ThenInclude(section => section.CourseLessons)
                    .ThenInclude(lesson => lesson.LessonResources)
            .Include(course => course.CourseSections)
                .ThenInclude(section => section.CourseQuizzes)
            .AsSplitQuery();

        return asTracking ? query : query.AsNoTracking();
    }

    private async Task<Shop> GetSellerShopAsync(Guid userId, CancellationToken cancellationToken)
    {
        var shop = await _dbContext.Shops
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.UserId == userId && item.IsActive == true,
                cancellationToken);

        if (shop is null)
        {
            throw new NotFoundException("Aktif magaza bulunamadi.");
        }

        return shop;
    }

    private async Task<Guid> ResolveCategoryIdAsync(string categoryIdOrSlug, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(categoryIdOrSlug))
        {
            throw new BadRequestException("Kategori zorunludur.");
        }

        var normalizedCategory = categoryIdOrSlug.Trim();
        var category = Guid.TryParse(normalizedCategory, out var categoryId)
            ? await _dbContext.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == categoryId && item.IsActive, cancellationToken)
            : await _dbContext.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Slug == normalizedCategory && item.IsActive, cancellationToken);

        if (category is null)
        {
            throw new NotFoundException("Kategori bulunamadi.");
        }

        return category.Id;
    }

    private async Task<Dictionary<Guid, int>> GetEnrolledCountsAsync(
        List<Guid> productIds,
        CancellationToken cancellationToken)
    {
        return await _dbContext.UserLibraries
            .AsNoTracking()
            .Where(item => productIds.Contains(item.ProductId))
            .GroupBy(item => item.ProductId)
            .Select(group => new { ProductId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.ProductId, item => item.Count, cancellationToken);
    }

    private SellerCourseListItemDto MapToListItem(Course course, int enrolledCount)
    {
        var activeSections = course.CourseSections.Where(section => section.IsActive).ToList();
        var activeLessons = activeSections
            .SelectMany(section => section.CourseLessons)
            .Where(lesson => lesson.IsActive)
            .ToList();

        return new SellerCourseListItemDto(
            CourseId: course.Id,
            ProductId: course.ProductId,
            ShopId: course.Product.ShopId,
            CategoryId: course.Product.CategoryId,
            Title: course.Product.Title,
            Description: course.Product.Description ?? string.Empty,
            Price: course.Product.Price,
            OriginalPrice: course.Product.OriginalPrice,
            CoverImageUrl: course.Product.CoverImageUrl,
            CoverImagePublicUrl: GeneratePublicAssetUrl(course.Product.CoverImageUrl),
            PreviewVideoUrl: course.Product.PreviewVideoUrl,
            PreviewVideoPublicUrl: GeneratePublicAssetUrl(course.Product.PreviewVideoUrl),
            Status: course.Product.Status,
            Tags: course.Product.Tags ?? new List<string>(),
            Level: course.Level,
            TotalDurationInMinutes: course.TotalDurationInMinutes,
            IsCertificateIncluded: course.IsCertificateIncluded,
            SectionCount: activeSections.Count,
            LessonCount: activeLessons.Count,
            EnrolledCount: enrolledCount,
            SalesCount: course.Product.SalesCount ?? 0,
            CreatedAt: course.CreatedAt,
            UpdatedAt: course.UpdatedAt);
    }

    private SellerCourseDetailDto MapToDetail(Course course, int enrolledCount, bool includeInactive)
    {
        return new SellerCourseDetailDto(
            CourseId: course.Id,
            ProductId: course.ProductId,
            ShopId: course.Product.ShopId,
            CategoryId: course.Product.CategoryId,
            Title: course.Product.Title,
            Description: course.Product.Description ?? string.Empty,
            Price: course.Product.Price,
            OriginalPrice: course.Product.OriginalPrice,
            CoverImageUrl: course.Product.CoverImageUrl,
            CoverImagePublicUrl: GeneratePublicAssetUrl(course.Product.CoverImageUrl),
            PreviewVideoUrl: course.Product.PreviewVideoUrl,
            PreviewVideoPublicUrl: GeneratePublicAssetUrl(course.Product.PreviewVideoUrl),
            Status: course.Product.Status,
            Tags: course.Product.Tags ?? new List<string>(),
            Level: course.Level,
            TotalDurationInMinutes: course.TotalDurationInMinutes,
            IsCertificateIncluded: course.IsCertificateIncluded,
            EnrolledCount: enrolledCount,
            SalesCount: course.Product.SalesCount ?? 0,
            CreatedAt: course.CreatedAt,
            UpdatedAt: course.UpdatedAt,
            Sections: course.CourseSections
                .Where(section => includeInactive || section.IsActive)
                .OrderBy(section => section.SortOrder)
                .Select(section => MapSection(section, includeInactive))
                .ToList());
    }

    private static SellerCourseSectionDto MapSection(CourseSection section, bool includeInactive)
    {
        return new SellerCourseSectionDto(
            Id: section.Id,
            CourseId: section.CourseId,
            Title: section.Title,
            SortOrder: section.SortOrder,
            IsActive: section.IsActive,
            CreatedAt: section.CreatedAt,
            UpdatedAt: section.UpdatedAt,
            Lessons: section.CourseLessons
                .Where(lesson => includeInactive || lesson.IsActive)
                .OrderBy(lesson => lesson.SortOrder)
                .Select(MapLesson)
                .ToList(),
            Quizzes: section.CourseQuizzes
                .Where(quiz => includeInactive || quiz.IsActive)
                .OrderBy(quiz => quiz.Title)
                .Select(quiz => new SellerCourseQuizDto(
                    Id: quiz.Id,
                    CourseSectionId: quiz.CourseSectionId,
                    Title: quiz.Title,
                    PassingScore: quiz.PassingScore,
                    IsActive: quiz.IsActive,
                    CreatedAt: quiz.CreatedAt,
                    UpdatedAt: quiz.UpdatedAt))
                .ToList());
    }

    private static SellerCourseLessonDto MapLesson(CourseLesson lesson)
    {
        return new SellerCourseLessonDto(
            Id: lesson.Id,
            CourseSectionId: lesson.CourseSectionId,
            Title: lesson.Title,
            VideoUrl: lesson.VideoUrl,
            VideoFileName: GetFileName(lesson.VideoUrl),
            DurationInSeconds: lesson.DurationInSeconds,
            SortOrder: lesson.SortOrder,
            IsFreePreview: lesson.IsFreePreview,
            IsActive: lesson.IsActive,
            CreatedAt: lesson.CreatedAt,
            UpdatedAt: lesson.UpdatedAt,
            Resources: lesson.LessonResources
                .OrderBy(resource => resource.Title)
                .Select(resource => new SellerCourseResourceDto(
                    Id: resource.Id,
                    CourseLessonId: resource.CourseLessonId,
                    Title: resource.Title,
                    FileUrl: resource.FileUrl,
                    FileName: GetFileName(resource.FileUrl),
                    ResourceType: resource.ResourceType,
                    CreatedAt: resource.CreatedAt,
                    UpdatedAt: resource.UpdatedAt))
                .ToList());
    }

    private static void AddSections(Course course, IEnumerable<CreateSellerCourseSectionDto> sections)
    {
        foreach (var sectionDto in sections)
        {
            var section = new CourseSection
            {
                Id = Guid.NewGuid(),
                Title = sectionDto.Title.Trim(),
                SortOrder = sectionDto.SortOrder,
                IsActive = sectionDto.IsActive,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            foreach (var lessonDto in sectionDto.Lessons)
            {
                section.CourseLessons.Add(CreateLessonEntity(lessonDto));
            }

            course.CourseSections.Add(section);
        }
    }

    private static CourseLesson CreateLessonEntity(CreateSellerCourseLessonDto dto)
    {
        var lesson = new CourseLesson
        {
            Id = Guid.NewGuid(),
            Title = dto.Title.Trim(),
            VideoUrl = dto.VideoUrl,
            DurationInSeconds = dto.DurationInSeconds,
            SortOrder = dto.SortOrder,
            IsFreePreview = dto.IsFreePreview,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var resourceDto in dto.Resources)
        {
            lesson.LessonResources.Add(CreateResourceEntity(resourceDto));
        }

        return lesson;
    }

    private static LessonResource CreateResourceEntity(UpsertSellerCourseResourceDto dto)
    {
        ValidateResourceType(dto.ResourceType);

        return new LessonResource
        {
            Id = Guid.NewGuid(),
            Title = dto.Title.Trim(),
            FileUrl = dto.FileUrl,
            ResourceType = NormalizeResourceType(dto.ResourceType),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
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

    private static ProductStatus? ParseProductStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        return status.Trim().ToLowerInvariant() switch
        {
            "draft" => ProductStatus.Draft,
            "published" => ProductStatus.Published,
            "archived" => ProductStatus.Archived,
            _ => throw new BadRequestException("Gecersiz kurs status degeri.")
        };
    }

    private static void ValidateLevel(string level)
    {
        if (!AllowedLevels.Contains(level))
        {
            throw new ValidationException("Level degeri Beginner, Intermediate veya Advanced olmalidir.");
        }
    }

    private static string NormalizeLevel(string level)
    {
        return AllowedLevels.First(allowedLevel => allowedLevel.Equals(level, StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateResources(IEnumerable<UpsertSellerCourseResourceDto> resources)
    {
        foreach (var resource in resources)
        {
            ValidateResourceType(resource.ResourceType);
        }
    }

    private static void ValidateResourceType(string resourceType)
    {
        if (!AllowedResourceTypes.Contains(resourceType))
        {
            throw new ValidationException("ResourceType degeri Document, SourceCode veya ExternalLink olmalidir.");
        }
    }

    private static string NormalizeResourceType(string resourceType)
    {
        return AllowedResourceTypes.First(allowedType => allowedType.Equals(resourceType, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> NormalizeTags(IEnumerable<string>? tags)
    {
        return tags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
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
