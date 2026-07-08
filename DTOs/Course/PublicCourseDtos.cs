namespace CraftoraApi.DTOs.Course;

public sealed record PublicCourseListResponseDto(
    IReadOnlyList<PublicCourseListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record PublicCourseListItemDto(
    Guid CourseId,
    Guid ProductId,
    string Title,
    string Description,
    string? CoverImagePublicUrl,
    decimal Price,
    decimal? OriginalPrice,
    string Currency,
    string Level,
    int TotalDurationInMinutes,
    int LessonCount,
    int SectionCount,
    decimal? RatingAverage,
    int ReviewCount,
    int SalesCount,
    bool IsPurchased,
    Guid ShopId,
    string ShopName,
    string ShopSlug,
    string? ShopLogoPublicUrl);

public sealed record PublicCourseDetailDto(
    Guid CourseId,
    Guid ProductId,
    string Title,
    string Description,
    string? CoverImagePublicUrl,
    string? PreviewVideoPublicUrl,
    decimal Price,
    decimal? OriginalPrice,
    string Currency,
    string Level,
    int TotalDurationInMinutes,
    int SectionCount,
    int LessonCount,
    bool IsPurchased,
    PublicCourseShopDto Shop,
    IReadOnlyList<PublicCourseSectionDto> Sections);

public sealed record PublicCourseShopDto(
    Guid Id,
    string ShopName,
    string Slug,
    string? LogoPublicUrl,
    string? ShortDescription);

public sealed record PublicCourseSectionDto(
    Guid Id,
    string Title,
    int SortOrder,
    IReadOnlyList<PublicCourseLessonDto> Lessons);

public sealed record PublicCourseLessonDto(
    Guid Id,
    string Title,
    int DurationInSeconds,
    int SortOrder,
    bool IsFreePreview,
    bool IsLocked,
    IReadOnlyList<PublicCourseResourceDto> Resources);

public sealed record PublicCourseResourceDto(
    Guid Id,
    string Title,
    string? FileName,
    string ResourceType,
    bool IsLocked);
