using CraftoraApi.Data;
using CraftoraApi.DTOs.Course;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class CourseLessonService : ICourseLessonService
{
    private static readonly HashSet<string> AllowedResourceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Document",
        "SourceCode",
        "ExternalLink"
    };

    private readonly AppDbContext _dbContext;

    public CourseLessonService(AppDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<CourseLessonResponseDto> CreateLessonAsync(Guid userId, CreateCourseLessonDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.CourseSectionId == Guid.Empty)
        {
            throw new ValidationException("CourseSectionId zorunludur.");
        }

        ValidateResources(dto.Resources);

        var section = await GetOwnedSectionAsync(userId, dto.CourseSectionId);

        var lesson = new CourseLesson
        {
            CourseSectionId = section.Id,
            Title = dto.Title.Trim(),
            VideoUrl = dto.VideoUrl,
            DurationInSeconds = dto.DurationInSeconds,
            SortOrder = dto.SortOrder,
            IsFreePreview = dto.IsFreePreview,
            IsActive = true
        };

        foreach (var resourceDto in dto.Resources)
        {
            lesson.LessonResources.Add(new LessonResource
            {
                Title = resourceDto.Title.Trim(),
                FileUrl = resourceDto.FileUrl,
                ResourceType = NormalizeResourceType(resourceDto.ResourceType)
            });
        }

        _dbContext.CourseLessons.Add(lesson);
        await _dbContext.SaveChangesAsync();

        return MapToResponse(lesson);
    }

    public async Task<CourseLessonResponseDto> UpdateLessonAsync(Guid userId, Guid lessonId, UpdateCourseLessonDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ValidateResources(dto.Resources);

        var lesson = await _dbContext.CourseLessons
            .Include(lesson => lesson.CourseSection)
                .ThenInclude(section => section.Course)
                    .ThenInclude(course => course.Product)
                        .ThenInclude(product => product.Shop)
            .Include(lesson => lesson.LessonResources)
            .FirstOrDefaultAsync(lesson => lesson.Id == lessonId);

        if (lesson is null)
        {
            throw new NotFoundException("Ders bulunamadi.");
        }

        EnsureCourseOwner(userId, lesson.CourseSection.Course);

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
            lesson.LessonResources.Add(new LessonResource
            {
                Title = resourceDto.Title.Trim(),
                FileUrl = resourceDto.FileUrl,
                ResourceType = NormalizeResourceType(resourceDto.ResourceType)
            });
        }

        await _dbContext.SaveChangesAsync();

        return MapToResponse(lesson);
    }

    public async Task DeleteLessonAsync(Guid userId, Guid lessonId)
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

        _dbContext.CourseLessons.Remove(lesson);
        await _dbContext.SaveChangesAsync();
    }

    private async Task<CourseSection> GetOwnedSectionAsync(Guid userId, Guid sectionId)
    {
        var section = await _dbContext.CourseSections
            .Include(section => section.Course)
                .ThenInclude(course => course.Product)
                    .ThenInclude(product => product.Shop)
            .FirstOrDefaultAsync(section => section.Id == sectionId);

        if (section is null)
        {
            throw new NotFoundException("Kurs bolumu bulunamadi.");
        }

        EnsureCourseOwner(userId, section.Course);

        return section;
    }

    private static void EnsureCourseOwner(Guid userId, Course course)
    {
        if (course.Product.Shop.UserId != userId)
        {
            throw new ForbiddenException("Bu dersi yonetme yetkiniz yok.");
        }
    }

    private static CourseLessonResponseDto MapToResponse(CourseLesson lesson)
    {
        return new CourseLessonResponseDto(
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
                .ToList());
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

    private static string NormalizeResourceType(string resourceType)
    {
        return AllowedResourceTypes.First(allowedType => allowedType.Equals(resourceType, StringComparison.OrdinalIgnoreCase));
    }
}
