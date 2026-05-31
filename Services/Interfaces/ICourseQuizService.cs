using CraftoraApi.DTOs.Course;

namespace CraftoraApi.Services.Interfaces;

public interface ICourseQuizService
{
    Task<CourseQuizResponseDto> AddQuizAsync(Guid userId, CreateCourseQuizDto dto);

    Task<CourseQuizResponseDto> UpdateQuizAsync(Guid userId, Guid quizId, UpdateCourseQuizDto dto);

    Task RemoveQuizAsync(Guid userId, Guid quizId);
}
