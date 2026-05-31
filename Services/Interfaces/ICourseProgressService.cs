using CraftoraApi.DTOs.Course;

namespace CraftoraApi.Services.Interfaces;

public interface ICourseProgressService
{
    Task UpdateProgressAsync(Guid userId, UpdateLessonProgressDto dto);

    Task<CourseProgressResponseDto> GetCourseProgressAsync(Guid userId, Guid courseId);
}
