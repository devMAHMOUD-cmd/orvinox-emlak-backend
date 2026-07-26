using CraftoraApi.Data;
using CraftoraApi.DTOs.Course;
using CraftoraApi.Infrastructure.Messaging;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Elasticsearch;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Redis;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class CourseService : ICourseService
{
    private const string PopularProductsCacheKey = "products:popular:public-active-shops:v4";
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
    private readonly ICacheService _cacheService;
    private readonly IRabbitMqPublisher _rabbitMqPublisher;
    private readonly IUploadService _uploadService;
    private readonly ILogger<CourseService> _logger;

    public CourseService(
        AppDbContext dbContext,
        ICacheService cacheService,
        IRabbitMqPublisher rabbitMqPublisher,
        IUploadService uploadService,
        ILogger<CourseService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _rabbitMqPublisher = rabbitMqPublisher ?? throw new ArgumentNullException(nameof(rabbitMqPublisher));
        _uploadService = uploadService ?? throw new ArgumentNullException(nameof(uploadService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CourseResponseDto> CreateCourseAsync(Guid userId, CreateCourseDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ValidateLevel(dto.Level);
        ValidateResources(dto.Sections.SelectMany(section => section.Lessons).SelectMany(lesson => lesson.Resources));

        var product = await GetOwnedProductAsync(userId, dto.ProductId);
        await ValidateAssetsAsync(userId, dto.Sections);

        var courseExists = await _dbContext.Courses.AnyAsync(course => course.ProductId == dto.ProductId);
        if (courseExists)
        {
            throw new ConflictException("Bu urun icin zaten bir egitim kaydi var.");
        }

        product.Type = ProductType.Course;

        var course = new Course
        {
            ProductId = dto.ProductId,
            Level = NormalizeLevel(dto.Level),
            TotalDurationInMinutes = dto.TotalDurationInMinutes,
            IsCertificateIncluded = dto.IsCertificateIncluded
        };

        AddSections(course, dto.Sections);

        _dbContext.Courses.Add(course);
        await _dbContext.SaveChangesAsync();
        await InvalidateProductCachesAsync(product.ShopId);
        await PublishProductIndexMessageAsync(product);

        return await GetCourseByIdAsync(course.Id);
    }

    public async Task<CourseResponseDto> UpdateCourseAsync(Guid userId, Guid courseId, UpdateCourseDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ValidateLevel(dto.Level);
        ValidateResources(dto.Sections.SelectMany(section => section.Lessons).SelectMany(lesson => lesson.Resources));
        await ValidateAssetsAsync(userId, dto.Sections);

        var course = await _dbContext.Courses
            .Include(course => course.CourseSections)
                .ThenInclude(section => section.CourseLessons)
                    .ThenInclude(lesson => lesson.LessonResources)
            .Include(course => course.CourseSections)
                .ThenInclude(section => section.CourseQuizzes)
            .FirstOrDefaultAsync(course => course.Id == courseId);

        if (course is null)
        {
            throw new NotFoundException("Egitim bulunamadi.");
        }

        await EnsureProductOwnershipAsync(userId, course.ProductId);

        course.Level = NormalizeLevel(dto.Level);
        course.TotalDurationInMinutes = dto.TotalDurationInMinutes;
        course.IsCertificateIncluded = dto.IsCertificateIncluded;
        course.UpdatedAt = DateTime.UtcNow;

        _dbContext.CourseSections.RemoveRange(course.CourseSections);
        course.CourseSections.Clear();
        AddSections(course, dto.Sections);

        await _dbContext.SaveChangesAsync();

        return await GetCourseByIdAsync(course.Id);
    }

    public async Task DeleteCourseAsync(Guid userId, Guid courseId)
    {
        var course = await _dbContext.Courses.FirstOrDefaultAsync(course => course.Id == courseId);
        if (course is null)
        {
            throw new NotFoundException("Egitim bulunamadi.");
        }

        var product = await GetOwnedProductAsync(userId, course.ProductId);

        product.Type = ProductType.DigitalFile;
        _dbContext.Courses.Remove(course);
        await _dbContext.SaveChangesAsync();
        await InvalidateProductCachesAsync(product.ShopId);
        await PublishProductIndexMessageAsync(product);
    }

    public async Task<CourseResponseDto> GetCourseByIdAsync(Guid courseId)
    {
        var course = await BuildCourseTreeQuery()
            .FirstOrDefaultAsync(course => course.Id == courseId);

        if (course is null)
        {
            throw new NotFoundException("Egitim bulunamadi.");
        }

        return MapToResponse(course);
    }

    public async Task<CourseResponseDto> GetCourseTreeByProductIdAsync(Guid productId)
    {
        var course = await BuildCourseTreeQuery()
            .FirstOrDefaultAsync(course => course.ProductId == productId);

        if (course is null)
        {
            throw new NotFoundException("Bu urune ait egitim bulunamadi.");
        }

        return MapToResponse(course);
    }

    private IQueryable<Course> BuildCourseTreeQuery()
    {
        return _dbContext.Courses
            .AsNoTracking()
            .Include(course => course.CourseSections)
                .ThenInclude(section => section.CourseLessons)
                    .ThenInclude(lesson => lesson.LessonResources)
            .Include(course => course.CourseSections)
                .ThenInclude(section => section.CourseQuizzes);
    }

    private async Task<Product> GetOwnedProductAsync(Guid userId, Guid productId)
    {
        var product = await _dbContext.Products
            .Include(product => product.Shop)
            .FirstOrDefaultAsync(product =>
                product.Id == productId &&
                product.IsActive == true);

        if (product is null)
        {
            throw new NotFoundException("Urun bulunamadi.");
        }

        if (product.Shop.UserId != userId)
        {
            throw new ForbiddenException("Bu urun icin egitim yonetme yetkiniz yok.");
        }

        return product;
    }

    private async Task EnsureProductOwnershipAsync(Guid userId, Guid productId)
    {
        _ = await GetOwnedProductAsync(userId, productId);
    }

    private async Task InvalidateProductCachesAsync(Guid shopId)
    {
        try
        {
            await _cacheService.RemoveAsync(PopularProductsCacheKey);
            var shopSlug = await _dbContext.Shops
                .AsNoTracking()
                .Where(shop => shop.Id == shopId)
                .Select(shop => shop.Slug)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(shopSlug))
            {
                await _cacheService.RemoveAsync(CacheKeys.PublicShopBySlug(shopSlug));
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Legacy course product caches could not be invalidated. ShopId: {ShopId}",
                shopId);
        }
    }

    private async Task PublishProductIndexMessageAsync(Product product)
    {
        try
        {
            await _rabbitMqPublisher.PublishProductSyncMessage(new ProductSyncMessage(
                ProductId: product.Id,
                Action: "Index",
                Document: new ProductDocument
                {
                    Id = product.Id,
                    Name = product.Title,
                    Description = product.Description,
                    Price = product.Price,
                    CategoryId = product.CategoryId,
                    ShopId = product.ShopId,
                    ShopName = product.Shop.ShopName,
                    IsActive = product.IsActive == true,
                    IsPublished = product.Status == ProductStatus.Published,
                    ShopIsActive = product.Shop.IsActive == true
                }));
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Legacy course product search message could not be published. ProductId: {ProductId}",
                product.Id);
        }
    }

    private async Task ValidateAssetsAsync(Guid userId, IEnumerable<CreateCourseSectionDto> sections)
    {
        foreach (var lesson in sections.SelectMany(section => section.Lessons))
        {
            ValidateUserScopedAsset(userId, lesson.VideoUrl);
            await ValidatePrivateAssetExistsAsync(userId, lesson.VideoUrl);
            foreach (var resource in lesson.Resources)
            {
                if (string.Equals(
                    resource.ResourceType,
                    "ExternalLink",
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                ValidateUserScopedAsset(userId, resource.FileUrl);
                await ValidatePrivateAssetExistsAsync(userId, resource.FileUrl);
            }
        }
    }

    private async Task ValidateAssetsAsync(Guid userId, IEnumerable<UpdateCourseSectionDto> sections)
    {
        foreach (var lesson in sections.SelectMany(section => section.Lessons))
        {
            ValidateUserScopedAsset(userId, lesson.VideoUrl);
            await ValidatePrivateAssetExistsAsync(userId, lesson.VideoUrl);
            foreach (var resource in lesson.Resources)
            {
                if (string.Equals(
                    resource.ResourceType,
                    "ExternalLink",
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                ValidateUserScopedAsset(userId, resource.FileUrl);
                await ValidatePrivateAssetExistsAsync(userId, resource.FileUrl);
            }
        }
    }

    private async Task ValidatePrivateAssetExistsAsync(Guid userId, string? urlOrObjectKey)
    {
        var objectKey = GetUserScopedObjectKey(userId, urlOrObjectKey);
        if (!string.IsNullOrWhiteSpace(objectKey))
        {
            await _uploadService.ValidateOwnedObjectAsync(userId, objectKey, isPublic: false);
        }
    }

    private static string? GetUserScopedObjectKey(Guid userId, string? urlOrObjectKey)
    {
        if (string.IsNullOrWhiteSpace(urlOrObjectKey))
        {
            return null;
        }

        var normalizedValue = Uri.TryCreate(urlOrObjectKey, UriKind.Absolute, out var uri)
            ? Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/')
            : urlOrObjectKey.Trim().TrimStart('/');
        var bucketPrefix = "private-products/";
        var bucketIndex = normalizedValue.IndexOf(bucketPrefix, StringComparison.OrdinalIgnoreCase);
        if (bucketIndex >= 0)
        {
            normalizedValue = normalizedValue[(bucketIndex + bucketPrefix.Length)..];
        }

        return normalizedValue.StartsWith(
            $"users/{userId:D}/",
            StringComparison.OrdinalIgnoreCase)
            ? normalizedValue
            : null;
    }

    private static void ValidateUserScopedAsset(Guid userId, string? urlOrObjectKey)
    {
        if (string.IsNullOrWhiteSpace(urlOrObjectKey))
        {
            return;
        }

        var normalizedValue = Uri.TryCreate(urlOrObjectKey, UriKind.Absolute, out var uri)
            ? Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/')
            : urlOrObjectKey.Trim().TrimStart('/');
        var usersSegmentIndex = normalizedValue.IndexOf("users/", StringComparison.OrdinalIgnoreCase);
        if (usersSegmentIndex < 0)
        {
            return;
        }

        var expectedPrefix = $"users/{userId:D}/";
        if (!normalizedValue[usersSegmentIndex..].StartsWith(
            expectedPrefix,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Baska bir kullaniciya ait dosya bu kursa baglanamaz.");
        }
    }

    private static void AddSections(Course course, IEnumerable<CreateCourseSectionDto> sections)
    {
        foreach (var sectionDto in sections)
        {
            var section = new CourseSection
            {
                Title = sectionDto.Title.Trim(),
                SortOrder = sectionDto.SortOrder,
                IsActive = sectionDto.IsActive
            };

            foreach (var lessonDto in sectionDto.Lessons)
            {
                var lesson = new CourseLesson
                {
                    Title = lessonDto.Title.Trim(),
                    VideoUrl = lessonDto.VideoUrl,
                    DurationInSeconds = lessonDto.DurationInSeconds,
                    SortOrder = lessonDto.SortOrder,
                    IsFreePreview = lessonDto.IsFreePreview,
                    IsActive = lessonDto.IsActive
                };

                foreach (var resourceDto in lessonDto.Resources)
                {
                    lesson.LessonResources.Add(new LessonResource
                    {
                        Title = resourceDto.Title.Trim(),
                        FileUrl = resourceDto.FileUrl,
                        ResourceType = NormalizeResourceType(resourceDto.ResourceType)
                    });
                }

                section.CourseLessons.Add(lesson);
            }

            foreach (var quizDto in sectionDto.Quizzes)
            {
                section.CourseQuizzes.Add(new CourseQuiz
                {
                    Title = quizDto.Title.Trim(),
                    PassingScore = quizDto.PassingScore,
                    IsActive = quizDto.IsActive
                });
            }

            course.CourseSections.Add(section);
        }
    }

    private static void AddSections(Course course, IEnumerable<UpdateCourseSectionDto> sections)
    {
        foreach (var sectionDto in sections)
        {
            var section = new CourseSection
            {
                Title = sectionDto.Title.Trim(),
                SortOrder = sectionDto.SortOrder,
                IsActive = sectionDto.IsActive
            };

            foreach (var lessonDto in sectionDto.Lessons)
            {
                var lesson = new CourseLesson
                {
                    Title = lessonDto.Title.Trim(),
                    VideoUrl = lessonDto.VideoUrl,
                    DurationInSeconds = lessonDto.DurationInSeconds,
                    SortOrder = lessonDto.SortOrder,
                    IsFreePreview = lessonDto.IsFreePreview,
                    IsActive = lessonDto.IsActive
                };

                foreach (var resourceDto in lessonDto.Resources)
                {
                    lesson.LessonResources.Add(new LessonResource
                    {
                        Title = resourceDto.Title.Trim(),
                        FileUrl = resourceDto.FileUrl,
                        ResourceType = NormalizeResourceType(resourceDto.ResourceType)
                    });
                }

                section.CourseLessons.Add(lesson);
            }

            foreach (var quizDto in sectionDto.Quizzes)
            {
                section.CourseQuizzes.Add(new CourseQuiz
                {
                    Title = quizDto.Title.Trim(),
                    PassingScore = quizDto.PassingScore,
                    IsActive = quizDto.IsActive
                });
            }

            course.CourseSections.Add(section);
        }
    }

    private static CourseResponseDto MapToResponse(Course course)
    {
        var sections = course.CourseSections
            .OrderBy(section => section.SortOrder)
            .Select(section => new CourseSectionResponseDto(
                Id: section.Id,
                CourseId: section.CourseId,
                Title: section.Title,
                SortOrder: section.SortOrder,
                IsActive: section.IsActive,
                CreatedAt: section.CreatedAt,
                UpdatedAt: section.UpdatedAt,
                Lessons: section.CourseLessons
                    .OrderBy(lesson => lesson.SortOrder)
                    .Select(lesson => new CourseLessonResponseDto(
                        Id: lesson.Id,
                        CourseSectionId: lesson.CourseSectionId,
                        Title: lesson.Title,
                        VideoUrl: lesson.VideoUrl,
                        DurationInSeconds: lesson.DurationInSeconds,
                        SortOrder: lesson.SortOrder,
                        IsFreePreview: lesson.IsFreePreview,
                        IsActive: lesson.IsActive,
                        CreatedAt: lesson.CreatedAt,
                        UpdatedAt: lesson.UpdatedAt,
                        Resources: lesson.LessonResources
                            .OrderBy(resource => resource.Title)
                            .Select(resource => new LessonResourceResponseDto(
                                Id: resource.Id,
                                CourseLessonId: resource.CourseLessonId,
                                Title: resource.Title,
                                FileUrl: resource.FileUrl,
                                ResourceType: resource.ResourceType,
                                CreatedAt: resource.CreatedAt,
                                UpdatedAt: resource.UpdatedAt))
                            .ToList()))
                    .ToList(),
                Quizzes: section.CourseQuizzes
                    .OrderBy(quiz => quiz.Title)
                    .Select(quiz => new CourseQuizResponseDto(
                        Id: quiz.Id,
                        CourseSectionId: quiz.CourseSectionId,
                        Title: quiz.Title,
                        PassingScore: quiz.PassingScore,
                        IsActive: quiz.IsActive,
                        CreatedAt: quiz.CreatedAt,
                        UpdatedAt: quiz.UpdatedAt))
                    .ToList()))
            .ToList();

        return new CourseResponseDto(
            Id: course.Id,
            ProductId: course.ProductId,
            Level: course.Level,
            TotalDurationInMinutes: course.TotalDurationInMinutes,
            IsCertificateIncluded: course.IsCertificateIncluded,
            CreatedAt: course.CreatedAt,
            UpdatedAt: course.UpdatedAt,
            Sections: sections);
    }

    private static void ValidateLevel(string level)
    {
        if (!AllowedLevels.Contains(level))
        {
            throw new ValidationException("Level degeri Beginner, Intermediate veya Advanced olmalidir.");
        }
    }

    private static void ValidateResources(IEnumerable<CreateLessonResourceDto> resources)
    {
        foreach (var resource in resources)
        {
            ValidateResourceType(resource.ResourceType);
        }
    }

    private static void ValidateResources(IEnumerable<UpdateLessonResourceDto> resources)
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

    private static string NormalizeLevel(string level)
    {
        return AllowedLevels.First(allowedLevel => allowedLevel.Equals(level, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeResourceType(string resourceType)
    {
        return AllowedResourceTypes.First(allowedType => allowedType.Equals(resourceType, StringComparison.OrdinalIgnoreCase));
    }
}
