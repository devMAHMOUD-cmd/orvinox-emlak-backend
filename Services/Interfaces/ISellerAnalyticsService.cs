using CraftoraApi.DTOs.Analytics;

namespace CraftoraApi.Services.Interfaces;

public interface ISellerAnalyticsService
{
    Task<SellerAnalyticsOverviewDto> GetOverviewAsync(
        Guid userId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default);

    Task<SellerAnalyticsFunnelDto> GetFunnelAsync(
        Guid userId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrafficSourceDto>> GetTrafficSourcesAsync(
        Guid userId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TopProductAnalyticsDto>> GetTopProductsAsync(
        Guid userId,
        DateTime? startDate,
        DateTime? endDate,
        int limit = 10,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CourseAnalyticsDto>> GetCoursesAsync(
        Guid userId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default);

    Task<CourseAnalyticsDetailDto> GetCourseDetailAsync(
        Guid userId,
        Guid courseId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default);
}
