using CraftoraApi.Data;
using CraftoraApi.DTOs.Course;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class CourseSectionService : ICourseSectionService
{
    private readonly AppDbContext _dbContext;

    public CourseSectionService(AppDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<CourseSectionResponseDto> CreateSectionAsync(Guid userId, CreateCourseSectionDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.CourseId == Guid.Empty)
        {
            throw new ValidationException("CourseId zorunludur.");
        }

        var course = await GetOwnedCourseAsync(userId, dto.CourseId);

        var section = new CourseSection
        {
            CourseId = course.Id,
            Title = dto.Title.Trim(),
            SortOrder = dto.SortOrder,
            IsActive = true
        };

        _dbContext.CourseSections.Add(section);
        await _dbContext.SaveChangesAsync();

        return MapToResponse(section);
    }

    public async Task<CourseSectionResponseDto> UpdateSectionAsync(Guid userId, Guid sectionId, UpdateCourseSectionDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var section = await _dbContext.CourseSections
            .Include(section => section.Course)
                .ThenInclude(course => course.Product)
                    .ThenInclude(product => product.Shop)
            .Include(section => section.CourseLessons)
                .ThenInclude(lesson => lesson.LessonResources)
            .Include(section => section.CourseQuizzes)
            .FirstOrDefaultAsync(section => section.Id == sectionId);

        if (section is null)
        {
            throw new NotFoundException("Kurs bolumu bulunamadi.");
        }

        EnsureCourseOwner(userId, section.Course);

        section.Title = dto.Title.Trim();
        section.SortOrder = dto.SortOrder;
        section.IsActive = dto.IsActive;
        section.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return MapToResponse(section);
    }

    public async Task DeleteSectionAsync(Guid userId, Guid sectionId)
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

        _dbContext.CourseSections.Remove(section);
        await _dbContext.SaveChangesAsync();
    }

    private async Task<Course> GetOwnedCourseAsync(Guid userId, Guid courseId)
    {
        var course = await _dbContext.Courses
            .Include(course => course.Product)
                .ThenInclude(product => product.Shop)
            .FirstOrDefaultAsync(course => course.Id == courseId);

        if (course is null)
        {
            throw new NotFoundException("Egitim bulunamadi.");
        }

        EnsureCourseOwner(userId, course);

        return course;
    }

    private static void EnsureCourseOwner(Guid userId, Course course)
    {
        if (course.Product.Shop.UserId != userId)
        {
            throw new ForbiddenException("Bu egitim bolumunu yonetme yetkiniz yok.");
        }
    }

    private static CourseSectionResponseDto MapToResponse(CourseSection section)
    {
        return new CourseSectionResponseDto(
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
                .ToList());
    }
}
