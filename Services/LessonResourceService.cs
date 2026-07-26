using CraftoraApi.Data;
using CraftoraApi.DTOs.Course;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class LessonResourceService : ILessonResourceService
{
    private static readonly HashSet<string> AllowedResourceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Document",
        "SourceCode",
        "ExternalLink"
    };

    private readonly AppDbContext _dbContext;
    private readonly IUploadService _uploadService;

    public LessonResourceService(
        AppDbContext dbContext,
        IUploadService uploadService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _uploadService = uploadService ?? throw new ArgumentNullException(nameof(uploadService));
    }

    public async Task<LessonResourceResponseDto> AddResourceAsync(Guid userId, CreateLessonResourceDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.CourseLessonId == Guid.Empty)
        {
            throw new ValidationException("CourseLessonId zorunludur.");
        }
        if (!string.Equals(
            dto.ResourceType,
            "ExternalLink",
            StringComparison.OrdinalIgnoreCase))
        {
            ValidateUserScopedAsset(userId, dto.FileUrl);
            var objectKey = GetUserScopedObjectKey(userId, dto.FileUrl);
            if (!string.IsNullOrWhiteSpace(objectKey))
            {
                await _uploadService.ValidateOwnedObjectAsync(userId, objectKey, isPublic: false);
            }
        }

        var lesson = await GetOwnedLessonAsync(userId, dto.CourseLessonId);

        var resource = new LessonResource
        {
            CourseLessonId = lesson.Id,
            Title = dto.Title.Trim(),
            FileUrl = dto.FileUrl,
            ResourceType = NormalizeResourceType(dto.ResourceType)
        };

        _dbContext.LessonResources.Add(resource);
        await _dbContext.SaveChangesAsync();

        return MapToResponse(resource);
    }

    public async Task RemoveResourceAsync(Guid userId, Guid resourceId)
    {
        var resource = await _dbContext.LessonResources
            .Include(resource => resource.CourseLesson)
                .ThenInclude(lesson => lesson.CourseSection)
                    .ThenInclude(section => section.Course)
                        .ThenInclude(course => course.Product)
                            .ThenInclude(product => product.Shop)
            .FirstOrDefaultAsync(resource => resource.Id == resourceId);

        if (resource is null)
        {
            throw new NotFoundException("Ders kaynagi bulunamadi.");
        }

        EnsureCourseOwner(userId, resource.CourseLesson.CourseSection.Course);

        _dbContext.LessonResources.Remove(resource);
        await _dbContext.SaveChangesAsync();
    }

    private async Task<CourseLesson> GetOwnedLessonAsync(Guid userId, Guid lessonId)
    {
        var lesson = await _dbContext.CourseLessons
            .Include(lesson => lesson.CourseSection)
                .ThenInclude(section => section.Course)
                    .ThenInclude(course => course.Product)
                        .ThenInclude(product => product.Shop)
            .FirstOrDefaultAsync(lesson => lesson.Id == lessonId);

        if (lesson is null)
        {
            throw new NotFoundException("Ders bulunamadi.");
        }

        EnsureCourseOwner(userId, lesson.CourseSection.Course);

        return lesson;
    }

    private static void EnsureCourseOwner(Guid userId, Course course)
    {
        if (course.Product.Shop.UserId != userId)
        {
            throw new ForbiddenException("Bu ders kaynagini yonetme yetkiniz yok.");
        }
    }

    private static LessonResourceResponseDto MapToResponse(LessonResource resource)
    {
        return new LessonResourceResponseDto(
            Id: resource.Id,
            CourseLessonId: resource.CourseLessonId,
            Title: resource.Title,
            FileUrl: resource.FileUrl,
            ResourceType: resource.ResourceType,
            CreatedAt: resource.CreatedAt,
            UpdatedAt: resource.UpdatedAt);
    }

    private static string NormalizeResourceType(string resourceType)
    {
        if (!AllowedResourceTypes.Contains(resourceType))
        {
            throw new ValidationException("ResourceType degeri Document, SourceCode veya ExternalLink olmalidir.");
        }

        return AllowedResourceTypes.First(allowedType => allowedType.Equals(resourceType, StringComparison.OrdinalIgnoreCase));
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
            throw new ForbiddenException("Baska bir kullaniciya ait dosya bu kaynaga baglanamaz.");
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
        const string bucketPrefix = "private-products/";
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
}
