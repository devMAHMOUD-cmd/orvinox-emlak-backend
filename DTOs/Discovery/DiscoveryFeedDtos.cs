using CraftoraApi.DTOs.Home;

namespace CraftoraApi.DTOs.Discovery;

public sealed record DiscoveryFeedResponseDto(
    string RankingVersion,
    IReadOnlyList<DiscoveryFeedItemDto> Items,
    string? NextCursor,
    bool HasMore);

public sealed record DiscoveryFeedItemDto(
    string ContentType,
    Guid ContentId,
    int Position,
    HomeReelDto? Media,
    HomeTrendingProductDto? Product,
    HomeFeaturedCourseDto? Course);
