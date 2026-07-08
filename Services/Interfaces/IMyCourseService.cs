using CraftoraApi.DTOs.Course;

namespace CraftoraApi.Services.Interfaces;

public interface IMyCourseService
{
    Task<IReadOnlyList<MyCourseListItemDto>> GetMyCoursesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<MyCourseDetailDto> GetMyCourseDetailAsync(
        Guid userId,
        Guid courseId,
        CancellationToken cancellationToken = default);

    Task<LessonVideoUrlResponseDto> GenerateLessonVideoUrlAsync(
        Guid userId,
        Guid lessonId,
        CancellationToken cancellationToken = default);

    Task<CourseResourceDownloadUrlResponseDto> GenerateResourceDownloadUrlAsync(
        Guid userId,
        Guid resourceId,
        CancellationToken cancellationToken = default);
}
