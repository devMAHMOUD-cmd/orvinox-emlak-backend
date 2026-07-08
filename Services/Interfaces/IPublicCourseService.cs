using CraftoraApi.DTOs.Course;

namespace CraftoraApi.Services.Interfaces;

public interface IPublicCourseService
{
    Task<PublicCourseListResponseDto> GetFeaturedCoursesAsync(
        Guid? currentUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PublicCourseListResponseDto> GetCoursesAsync(
        Guid? currentUserId,
        string? query,
        Guid? categoryId,
        string? level,
        decimal? minPrice,
        decimal? maxPrice,
        string? sort,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PublicCourseDetailDto> GetPublicCourseDetailAsync(
        Guid? currentUserId,
        Guid courseId,
        CancellationToken cancellationToken = default);
}
