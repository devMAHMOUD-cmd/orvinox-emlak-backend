using CraftoraApi.Data;
using CraftoraApi.DTOs.Course;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class CourseQuizService : ICourseQuizService
{
    private readonly AppDbContext _dbContext;

    public CourseQuizService(AppDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<CourseQuizResponseDto> AddQuizAsync(Guid userId, CreateCourseQuizDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.CourseSectionId == Guid.Empty)
        {
            throw new ValidationException("CourseSectionId zorunludur.");
        }

        var section = await GetOwnedSectionAsync(userId, dto.CourseSectionId);

        var quiz = new CourseQuiz
        {
            CourseSectionId = section.Id,
            Title = dto.Title.Trim(),
            PassingScore = dto.PassingScore,
            IsActive = true
        };

        _dbContext.CourseQuizzes.Add(quiz);
        await _dbContext.SaveChangesAsync();

        return MapToResponse(quiz);
    }

    public async Task<CourseQuizResponseDto> UpdateQuizAsync(Guid userId, Guid quizId, UpdateCourseQuizDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var quiz = await _dbContext.CourseQuizzes
            .Include(quiz => quiz.CourseSection)
                .ThenInclude(section => section.Course)
                    .ThenInclude(course => course.Product)
                        .ThenInclude(product => product.Shop)
            .FirstOrDefaultAsync(quiz => quiz.Id == quizId);

        if (quiz is null)
        {
            throw new NotFoundException("Bolum testi bulunamadi.");
        }

        EnsureCourseOwner(userId, quiz.CourseSection.Course);

        quiz.Title = dto.Title.Trim();
        quiz.PassingScore = dto.PassingScore;
        quiz.IsActive = dto.IsActive;
        quiz.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return MapToResponse(quiz);
    }

    public async Task RemoveQuizAsync(Guid userId, Guid quizId)
    {
        var quiz = await _dbContext.CourseQuizzes
            .Include(quiz => quiz.CourseSection)
                .ThenInclude(section => section.Course)
                    .ThenInclude(course => course.Product)
                        .ThenInclude(product => product.Shop)
            .FirstOrDefaultAsync(quiz => quiz.Id == quizId);

        if (quiz is null)
        {
            throw new NotFoundException("Bolum testi bulunamadi.");
        }

        EnsureCourseOwner(userId, quiz.CourseSection.Course);

        _dbContext.CourseQuizzes.Remove(quiz);
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
            throw new ForbiddenException("Bu bolum testini yonetme yetkiniz yok.");
        }
    }

    private static CourseQuizResponseDto MapToResponse(CourseQuiz quiz)
    {
        return new CourseQuizResponseDto(
            Id: quiz.Id,
            CourseSectionId: quiz.CourseSectionId,
            Title: quiz.Title,
            PassingScore: quiz.PassingScore,
            IsActive: quiz.IsActive,
            CreatedAt: quiz.CreatedAt,
            UpdatedAt: quiz.UpdatedAt);
    }
}
