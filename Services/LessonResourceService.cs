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

    public LessonResourceService(AppDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<LessonResourceResponseDto> AddResourceAsync(Guid userId, CreateLessonResourceDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.CourseLessonId == Guid.Empty)
        {
            throw new ValidationException("CourseLessonId zorunludur.");
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
}
