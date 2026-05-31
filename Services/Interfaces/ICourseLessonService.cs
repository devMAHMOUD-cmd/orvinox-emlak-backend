using CraftoraApi.DTOs.Course;

namespace CraftoraApi.Services.Interfaces;

public interface ICourseLessonService
{
    Task<CourseLessonResponseDto> CreateLessonAsync(Guid userId, CreateCourseLessonDto dto);

    Task<CourseLessonResponseDto> UpdateLessonAsync(Guid userId, Guid lessonId, UpdateCourseLessonDto dto);

    Task DeleteLessonAsync(Guid userId, Guid lessonId);
}
