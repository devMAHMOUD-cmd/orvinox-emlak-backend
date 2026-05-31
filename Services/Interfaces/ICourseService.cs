using CraftoraApi.DTOs.Course;

namespace CraftoraApi.Services.Interfaces;

public interface ICourseService
{
    Task<CourseResponseDto> CreateCourseAsync(Guid userId, CreateCourseDto dto);

    Task<CourseResponseDto> UpdateCourseAsync(Guid userId, Guid courseId, UpdateCourseDto dto);

    Task DeleteCourseAsync(Guid userId, Guid courseId);

    Task<CourseResponseDto> GetCourseByIdAsync(Guid courseId);

    Task<CourseResponseDto> GetCourseTreeByProductIdAsync(Guid productId);
}
