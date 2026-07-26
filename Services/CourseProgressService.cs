using CraftoraApi.Data;
using CraftoraApi.DTOs.Course;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class CourseProgressService : ICourseProgressService
{
    private readonly AppDbContext _dbContext;

    public CourseProgressService(AppDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task UpdateProgressAsync(Guid userId, UpdateLessonProgressDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var lesson = await _dbContext.CourseLessons
            .AsNoTracking()
            .Where(lesson =>
                lesson.Id == dto.CourseLessonId &&
                lesson.IsActive)
            .Select(lesson => new
            {
                CourseId = lesson.CourseSection.CourseId,
                lesson.DurationInSeconds
            })
            .FirstOrDefaultAsync();

        if (lesson is null)
        {
            throw new NotFoundException("Ders bulunamadi.");
        }

        await EnsureCourseAccessAsync(userId, lesson.CourseId);

        var progress = await _dbContext.UserLessonProgresses.FirstOrDefaultAsync(progress =>
            progress.UserId == userId &&
            progress.CourseLessonId == dto.CourseLessonId);
        var watchedSeconds = Math.Min(
            dto.WatchedSeconds,
            Math.Max(lesson.DurationInSeconds, 0));

        if (progress is null)
        {
            progress = new UserLessonProgress
            {
                UserId = userId,
                CourseLessonId = dto.CourseLessonId,
                IsCompleted = dto.IsCompleted,
                WatchedSeconds = watchedSeconds,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.UserLessonProgresses.Add(progress);
        }
        else
        {
            progress.IsCompleted = progress.IsCompleted || dto.IsCompleted;
            progress.WatchedSeconds = Math.Max(progress.WatchedSeconds, watchedSeconds);
            progress.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task<CourseProgressResponseDto> GetCourseProgressAsync(Guid userId, Guid courseId)
    {
        await EnsureCourseAccessAsync(userId, courseId);

        var lessonIdsQuery = _dbContext.CourseLessons
            .AsNoTracking()
            .Where(lesson =>
                lesson.IsActive &&
                lesson.CourseSection.IsActive &&
                lesson.CourseSection.CourseId == courseId)
            .Select(lesson => lesson.Id);

        var totalLessons = await lessonIdsQuery.CountAsync();
        var completedLessons = await _dbContext.UserLessonProgresses
            .AsNoTracking()
            .Where(progress =>
                progress.UserId == userId &&
                progress.IsCompleted &&
                lessonIdsQuery.Contains(progress.CourseLessonId))
            .CountAsync();

        var completionPercentage = totalLessons == 0
            ? 0
            : Math.Round((double)completedLessons / totalLessons * 100, 2);

        return new CourseProgressResponseDto(
            CourseId: courseId,
            TotalLessons: totalLessons,
            CompletedLessons: completedLessons,
            CompletionPercentage: completionPercentage);
    }

    private async Task EnsureCourseAccessAsync(Guid userId, Guid courseId)
    {
        var course = await _dbContext.Courses
            .AsNoTracking()
            .Where(item => item.Id == courseId)
            .Select(item => new
            {
                item.ProductId,
                ShopOwnerId = item.Product.Shop.UserId
            })
            .FirstOrDefaultAsync();

        if (course is null)
        {
            throw new NotFoundException("Egitim bulunamadi.");
        }

        var hasPurchasedCourse = await _dbContext.UserLibraries
            .AsNoTracking()
            .AnyAsync(item =>
                item.UserId == userId &&
                item.ProductId == course.ProductId);

        if (hasPurchasedCourse || course.ShopOwnerId == userId)
        {
            return;
        }

        var isAdmin = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(item => item.Id == userId && item.Role == UserRole.Admin);

        if (!isAdmin)
        {
            throw new ForbiddenException("Bu derse erisim icin kursu satin almalisiniz.");
        }
    }
}
