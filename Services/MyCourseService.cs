using CraftoraApi.Data;
using CraftoraApi.DTOs.Course;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class MyCourseService : IMyCourseService
{
    private const string PublicAssetsBucketName = "public-assets";
    private const string PrivateProductsBucketName = "private-products";
    private const int PublicMediaUrlExpiryMinutes = 60;
    private const int PrivateVideoUrlExpiryMinutes = 60;

    private readonly AppDbContext _dbContext;
    private readonly IStorageService _storageService;

    public MyCourseService(
        AppDbContext dbContext,
        IStorageService storageService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
    }

    public async Task<IReadOnlyList<MyCourseListItemDto>> GetMyCoursesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var libraryItems = await _dbContext.UserLibraries
            .AsNoTracking()
            .Include(item => item.Product)
                .ThenInclude(product => product.Courses)
                    .ThenInclude(course => course.CourseSections)
                        .ThenInclude(section => section.CourseLessons)
            .Where(item =>
                item.UserId == userId &&
                item.Product.Type == ProductType.Course)
            .OrderByDescending(item => item.LastAccessedAt ?? item.PurchasedAt)
            .ToListAsync(cancellationToken);

        var courseIds = libraryItems
            .SelectMany(item => item.Product.Courses)
            .Select(course => course.Id)
            .ToList();
        var progress = await BuildProgressLookupAsync(userId, courseIds, cancellationToken);

        return libraryItems
            .SelectMany(item => item.Product.Courses.Select(course => MapToListItem(item, course, progress)))
            .ToList();
    }

    public async Task<MyCourseDetailDto> GetMyCourseDetailAsync(
        Guid userId,
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        var course = await GetAccessibleCourseTreeAsync(userId, courseId, cancellationToken);
        var libraryItem = await _dbContext.UserLibraries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.UserId == userId && item.ProductId == course.ProductId,
                cancellationToken);

        if (libraryItem is null)
        {
            throw new ForbiddenException("Bu kursa erisim yetkiniz yok.");
        }

        var progress = await BuildLessonProgressLookupAsync(
            userId,
            course.CourseSections.SelectMany(section => section.CourseLessons).Select(lesson => lesson.Id).ToList(),
            cancellationToken);

        return MapToDetail(libraryItem, course, progress);
    }

    public async Task<LessonVideoUrlResponseDto> GenerateLessonVideoUrlAsync(
        Guid userId,
        Guid lessonId,
        CancellationToken cancellationToken = default)
    {
        var lesson = await _dbContext.CourseLessons
            .AsNoTracking()
            .Include(item => item.CourseSection)
                .ThenInclude(section => section.Course)
                    .ThenInclude(course => course.Product)
                        .ThenInclude(product => product.Shop)
            .FirstOrDefaultAsync(item => item.Id == lessonId && item.IsActive, cancellationToken);

        if (lesson is null)
        {
            throw new NotFoundException("Ders bulunamadi.");
        }

        if (string.IsNullOrWhiteSpace(lesson.VideoUrl))
        {
            throw new NotFoundException("Ders videosu bulunamadi.");
        }

        var productId = lesson.CourseSection.Course.ProductId;
        var hasAccess = await _dbContext.UserLibraries
            .AsNoTracking()
            .AnyAsync(item => item.UserId == userId && item.ProductId == productId, cancellationToken);
        var isCourseOwner = lesson.CourseSection.Course.Product.Shop.UserId == userId;
        var isAdmin = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(item => item.Id == userId && item.Role == UserRole.Admin, cancellationToken);

        if (!hasAccess && !isCourseOwner && !isAdmin && !lesson.IsFreePreview)
        {
            throw new ForbiddenException("Bu ders videosuna erisim yetkiniz yok.");
        }

        var videoUrl = _storageService.GeneratePresignedDownloadUrl(
            PrivateProductsBucketName,
            lesson.VideoUrl,
            PrivateVideoUrlExpiryMinutes);

        return new LessonVideoUrlResponseDto(
            VideoUrl: videoUrl,
            ExpiresAt: DateTime.UtcNow.AddMinutes(PrivateVideoUrlExpiryMinutes),
            FileName: GetFileName(lesson.VideoUrl) ?? "lesson-video");
    }

    public async Task<CourseResourceDownloadUrlResponseDto> GenerateResourceDownloadUrlAsync(
        Guid userId,
        Guid resourceId,
        CancellationToken cancellationToken = default)
    {
        var resource = await _dbContext.LessonResources
            .AsNoTracking()
            .Include(item => item.CourseLesson)
                .ThenInclude(lesson => lesson.CourseSection)
                    .ThenInclude(section => section.Course)
                        .ThenInclude(course => course.Product)
                            .ThenInclude(product => product.Shop)
            .FirstOrDefaultAsync(item => item.Id == resourceId, cancellationToken);

        if (resource is null)
        {
            throw new NotFoundException("Kaynak dosya bulunamadi.");
        }

        if (string.IsNullOrWhiteSpace(resource.FileUrl))
        {
            throw new NotFoundException("Kaynak dosya adresi bulunamadi.");
        }

        var product = resource.CourseLesson.CourseSection.Course.Product;
        var hasAccess = await _dbContext.UserLibraries
            .AsNoTracking()
            .AnyAsync(item => item.UserId == userId && item.ProductId == product.Id, cancellationToken);
        var isCourseOwner = product.Shop.UserId == userId;
        var isAdmin = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(item => item.Id == userId && item.Role == UserRole.Admin, cancellationToken);

        if (!hasAccess && !isCourseOwner && !isAdmin)
        {
            throw new ForbiddenException("Bu kaynak dosyaya erisim yetkiniz yok.");
        }

        var downloadUrl = _storageService.GeneratePresignedDownloadUrl(
            PrivateProductsBucketName,
            resource.FileUrl,
            PrivateVideoUrlExpiryMinutes);

        return new CourseResourceDownloadUrlResponseDto(
            DownloadUrl: downloadUrl,
            ExpiresAt: DateTime.UtcNow.AddMinutes(PrivateVideoUrlExpiryMinutes),
            FileName: GetFileName(resource.FileUrl) ?? resource.Title);
    }

    private async Task<Course> GetAccessibleCourseTreeAsync(
        Guid userId,
        Guid courseId,
        CancellationToken cancellationToken)
    {
        var course = await _dbContext.Courses
            .AsNoTracking()
            .Include(item => item.Product)
            .Include(item => item.CourseSections)
                .ThenInclude(section => section.CourseLessons)
                    .ThenInclude(lesson => lesson.LessonResources)
            .Where(item => item.Id == courseId)
            .AsSplitQuery()
            .FirstOrDefaultAsync(cancellationToken);

        if (course is null)
        {
            throw new NotFoundException("Kurs bulunamadi.");
        }

        var hasAccess = await _dbContext.UserLibraries
            .AsNoTracking()
            .AnyAsync(item => item.UserId == userId && item.ProductId == course.ProductId, cancellationToken);

        if (!hasAccess)
        {
            throw new ForbiddenException("Bu kursa erisim yetkiniz yok.");
        }

        return course;
    }

    private async Task<Dictionary<Guid, CourseProgressSnapshot>> BuildProgressLookupAsync(
        Guid userId,
        List<Guid> courseIds,
        CancellationToken cancellationToken)
    {
        if (courseIds.Count == 0)
        {
            return new Dictionary<Guid, CourseProgressSnapshot>();
        }

        var totalLessons = await _dbContext.CourseLessons
            .AsNoTracking()
            .Where(lesson =>
                courseIds.Contains(lesson.CourseSection.CourseId) &&
                lesson.IsActive &&
                lesson.CourseSection.IsActive)
            .GroupBy(lesson => lesson.CourseSection.CourseId)
            .Select(group => new { CourseId = group.Key, TotalLessons = group.Count() })
            .ToDictionaryAsync(item => item.CourseId, item => item.TotalLessons, cancellationToken);

        var completedLessons = await _dbContext.UserLessonProgresses
            .AsNoTracking()
            .Where(progress =>
                progress.UserId == userId &&
                progress.IsCompleted &&
                courseIds.Contains(progress.CourseLesson.CourseSection.CourseId))
            .GroupBy(progress => progress.CourseLesson.CourseSection.CourseId)
            .Select(group => new { CourseId = group.Key, CompletedLessons = group.Count() })
            .ToDictionaryAsync(item => item.CourseId, item => item.CompletedLessons, cancellationToken);

        return courseIds.ToDictionary(
            courseId => courseId,
            courseId =>
            {
                totalLessons.TryGetValue(courseId, out var total);
                completedLessons.TryGetValue(courseId, out var completed);

                return new CourseProgressSnapshot(
                    TotalLessons: total,
                    CompletedLessons: completed,
                    CompletionPercentage: CalculateRate(completed, total));
            });
    }

    private async Task<Dictionary<Guid, UserLessonProgress>> BuildLessonProgressLookupAsync(
        Guid userId,
        List<Guid> lessonIds,
        CancellationToken cancellationToken)
    {
        return await _dbContext.UserLessonProgresses
            .AsNoTracking()
            .Where(progress =>
                progress.UserId == userId &&
                lessonIds.Contains(progress.CourseLessonId))
            .ToDictionaryAsync(progress => progress.CourseLessonId, cancellationToken);
    }

    private MyCourseListItemDto MapToListItem(
        UserLibrary libraryItem,
        Course course,
        Dictionary<Guid, CourseProgressSnapshot> progressLookup)
    {
        progressLookup.TryGetValue(course.Id, out var progress);

        return new MyCourseListItemDto(
            CourseId: course.Id,
            ProductId: course.ProductId,
            Title: course.Product.Title,
            Description: course.Product.Description ?? string.Empty,
            CoverImageUrl: course.Product.CoverImageUrl,
            CoverImagePublicUrl: GeneratePublicAssetUrl(course.Product.CoverImageUrl),
            PreviewVideoUrl: course.Product.PreviewVideoUrl,
            PreviewVideoPublicUrl: GeneratePublicAssetUrl(course.Product.PreviewVideoUrl),
            Level: course.Level,
            TotalDurationInMinutes: course.TotalDurationInMinutes,
            TotalLessons: progress?.TotalLessons ?? 0,
            CompletedLessons: progress?.CompletedLessons ?? 0,
            CompletionPercentage: progress?.CompletionPercentage ?? 0,
            PurchasedAt: libraryItem.PurchasedAt,
            LastAccessedAt: libraryItem.LastAccessedAt);
    }

    private MyCourseDetailDto MapToDetail(
        UserLibrary libraryItem,
        Course course,
        Dictionary<Guid, UserLessonProgress> progressLookup)
    {
        var sections = course.CourseSections
            .Where(section => section.IsActive)
            .OrderBy(section => section.SortOrder)
            .Select(section => new MyCourseSectionDto(
                Id: section.Id,
                Title: section.Title,
                SortOrder: section.SortOrder,
                Lessons: section.CourseLessons
                    .Where(lesson => lesson.IsActive)
                    .OrderBy(lesson => lesson.SortOrder)
                    .Select(lesson =>
                    {
                        progressLookup.TryGetValue(lesson.Id, out var progress);

                        return new MyCourseLessonDto(
                            Id: lesson.Id,
                            Title: lesson.Title,
                            DurationInSeconds: lesson.DurationInSeconds,
                            SortOrder: lesson.SortOrder,
                            IsFreePreview: lesson.IsFreePreview,
                            IsCompleted: progress?.IsCompleted ?? false,
                            WatchedSeconds: progress?.WatchedSeconds ?? 0,
                            Resources: lesson.LessonResources
                                .OrderBy(resource => resource.Title)
                                .Select(resource => new MyCourseResourceDto(
                                    Id: resource.Id,
                                    Title: resource.Title,
                                    ResourceType: resource.ResourceType,
                                    FileName: GetFileName(resource.FileUrl)))
                                .ToList());
                    })
                    .ToList()))
            .ToList();

        var totalLessons = sections.Sum(section => section.Lessons.Count);
        var completedLessons = sections.Sum(section => section.Lessons.Count(lesson => lesson.IsCompleted));

        return new MyCourseDetailDto(
            CourseId: course.Id,
            ProductId: course.ProductId,
            Title: course.Product.Title,
            Description: course.Product.Description ?? string.Empty,
            CoverImageUrl: course.Product.CoverImageUrl,
            CoverImagePublicUrl: GeneratePublicAssetUrl(course.Product.CoverImageUrl),
            PreviewVideoUrl: course.Product.PreviewVideoUrl,
            PreviewVideoPublicUrl: GeneratePublicAssetUrl(course.Product.PreviewVideoUrl),
            Level: course.Level,
            TotalDurationInMinutes: course.TotalDurationInMinutes,
            IsCertificateIncluded: course.IsCertificateIncluded,
            TotalLessons: totalLessons,
            CompletedLessons: completedLessons,
            CompletionPercentage: CalculateRate(completedLessons, totalLessons),
            PurchasedAt: libraryItem.PurchasedAt,
            LastAccessedAt: libraryItem.LastAccessedAt,
            Sections: sections);
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

    private static double CalculateRate(int numerator, int denominator)
    {
        return denominator <= 0
            ? 0
            : Math.Round(numerator * 100d / denominator, 2);
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

    private sealed record CourseProgressSnapshot(
        int TotalLessons,
        int CompletedLessons,
        double CompletionPercentage);
}
