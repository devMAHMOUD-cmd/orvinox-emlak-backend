namespace CraftoraApi.DTOs.Course;

public sealed record MyCourseListItemDto(
    Guid CourseId,
    Guid ProductId,
    string Title,
    string Description,
    string? CoverImageUrl,
    string? CoverImagePublicUrl,
    string? PreviewVideoUrl,
    string? PreviewVideoPublicUrl,
    string Level,
    int TotalDurationInMinutes,
    int TotalLessons,
    int CompletedLessons,
    double CompletionPercentage,
    DateTime? PurchasedAt,
    DateTime? LastAccessedAt);

public sealed record MyCourseDetailDto(
    Guid CourseId,
    Guid ProductId,
    string Title,
    string Description,
    string? CoverImageUrl,
    string? CoverImagePublicUrl,
    string? PreviewVideoUrl,
    string? PreviewVideoPublicUrl,
    string Level,
    int TotalDurationInMinutes,
    bool IsCertificateIncluded,
    int TotalLessons,
    int CompletedLessons,
    double CompletionPercentage,
    DateTime? PurchasedAt,
    DateTime? LastAccessedAt,
    IReadOnlyList<MyCourseSectionDto> Sections);

public sealed record MyCourseSectionDto(
    Guid Id,
    string Title,
    int SortOrder,
    IReadOnlyList<MyCourseLessonDto> Lessons);

public sealed record MyCourseLessonDto(
    Guid Id,
    string Title,
    int DurationInSeconds,
    int SortOrder,
    bool IsFreePreview,
    bool IsCompleted,
    int WatchedSeconds,
    IReadOnlyList<MyCourseResourceDto> Resources);

public sealed record MyCourseResourceDto(
    Guid Id,
    string Title,
    string ResourceType,
    string? FileName);

public sealed record LessonVideoUrlResponseDto(
    string VideoUrl,
    DateTime ExpiresAt,
    string FileName);

public sealed record CourseResourceDownloadUrlResponseDto(
    string DownloadUrl,
    DateTime ExpiresAt,
    string FileName);
