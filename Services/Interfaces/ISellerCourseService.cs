using CraftoraApi.DTOs.Course;

namespace CraftoraApi.Services.Interfaces;

public interface ISellerCourseService
{
    Task<SellerCourseListResponseDto> GetSellerCoursesAsync(
        Guid userId,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<SellerCourseDetailDto> GetSellerCourseDetailAsync(
        Guid userId,
        Guid courseId,
        CancellationToken cancellationToken = default);

    Task<SellerCourseDetailDto> CreateSellerCourseAsync(
        Guid userId,
        CreateSellerCourseDto dto,
        CancellationToken cancellationToken = default);

    Task<SellerCourseDetailDto> UpdateSellerCourseAsync(
        Guid userId,
        Guid courseId,
        UpdateSellerCourseDto dto,
        CancellationToken cancellationToken = default);

    Task ArchiveSellerCourseAsync(
        Guid userId,
        Guid courseId,
        CancellationToken cancellationToken = default);

    Task<SellerCourseSectionDto> CreateSectionAsync(
        Guid userId,
        Guid courseId,
        CreateSellerCourseSectionDto dto,
        CancellationToken cancellationToken = default);

    Task<SellerCourseSectionDto> UpdateSectionAsync(
        Guid userId,
        Guid sectionId,
        UpdateSellerCourseSectionDto dto,
        CancellationToken cancellationToken = default);

    Task DeleteSectionAsync(
        Guid userId,
        Guid sectionId,
        CancellationToken cancellationToken = default);

    Task<SellerCourseLessonDto> CreateLessonAsync(
        Guid userId,
        Guid sectionId,
        CreateSellerCourseLessonDto dto,
        CancellationToken cancellationToken = default);

    Task<SellerCourseLessonDto> UpdateLessonAsync(
        Guid userId,
        Guid lessonId,
        UpdateSellerCourseLessonDto dto,
        CancellationToken cancellationToken = default);

    Task DeleteLessonAsync(
        Guid userId,
        Guid lessonId,
        CancellationToken cancellationToken = default);
}
