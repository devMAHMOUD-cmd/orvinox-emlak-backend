using CraftoraApi.Data;
using CraftoraApi.DTOs.Course;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
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

        var lesson = await _dbContext.CourseLessons
            .AsNoTracking()
            .Where(lesson =>
                lesson.Id == dto.CourseLessonId &&
                lesson.IsActive)
            .Select(lesson => new
            {
                CourseId = lesson.CourseSection.CourseId
            })
            .FirstOrDefaultAsync();

        if (lesson is null)
        {
            throw new NotFoundException("Ders bulunamadi.");
        }

        await EnsureCourseAccessAsync(userId, lesson.CourseId);

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

    private static string GetProgressCacheKey(Guid userId, Guid lessonId)
    {
        return $"progress:user:{userId}:lesson:{lessonId}";
    }
}
