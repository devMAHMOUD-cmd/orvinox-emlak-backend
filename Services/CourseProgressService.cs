using CraftoraApi.Data;
using CraftoraApi.DTOs.Course;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Redis;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class CourseProgressService : ICourseProgressService
{
    private static readonly TimeSpan ProgressCacheTtl = TimeSpan.FromHours(1);

    private readonly AppDbContext _dbContext;
    private readonly ICacheService _cacheService;

    public CourseProgressService(
        AppDbContext dbContext,
        ICacheService cacheService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
    }

    public async Task UpdateProgressAsync(Guid userId, UpdateLessonProgressDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var lessonExists = await _dbContext.CourseLessons.AnyAsync(lesson =>
            lesson.Id == dto.CourseLessonId &&
            lesson.IsActive);

        if (!lessonExists)
        {
            throw new NotFoundException("Ders bulunamadi.");
        }

        var progressCacheKey = GetProgressCacheKey(userId, dto.CourseLessonId);
        await _cacheService.SetAsync(progressCacheKey, dto, ProgressCacheTtl);

        if (!dto.IsCompleted)
        {
            return;
        }

        var cachedProgress = await _cacheService.GetAsync<UpdateLessonProgressDto>(progressCacheKey) ?? dto;

        var progress = await _dbContext.UserLessonProgresses.FirstOrDefaultAsync(progress =>
            progress.UserId == userId &&
            progress.CourseLessonId == cachedProgress.CourseLessonId);

        if (progress is null)
        {
            progress = new UserLessonProgress
            {
                UserId = userId,
                CourseLessonId = cachedProgress.CourseLessonId,
                IsCompleted = cachedProgress.IsCompleted,
                WatchedSeconds = cachedProgress.WatchedSeconds,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.UserLessonProgresses.Add(progress);
        }
        else
        {
            progress.IsCompleted = cachedProgress.IsCompleted;
            progress.WatchedSeconds = cachedProgress.WatchedSeconds;
            progress.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();
        await _cacheService.RemoveAsync(progressCacheKey);
    }

    public async Task<CourseProgressResponseDto> GetCourseProgressAsync(Guid userId, Guid courseId)
    {
        var courseExists = await _dbContext.Courses.AnyAsync(course => course.Id == courseId);
        if (!courseExists)
        {
            throw new NotFoundException("Egitim bulunamadi.");
        }

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

    private static string GetProgressCacheKey(Guid userId, Guid lessonId)
    {
        return $"progress:user:{userId}:lesson:{lessonId}";
    }
}
