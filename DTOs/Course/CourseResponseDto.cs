namespace CraftoraApi.DTOs.Course;

public sealed record CourseResponseDto(
    Guid Id,
    Guid ProductId,
    string Level,
    int TotalDurationInMinutes,
    bool IsCertificateIncluded,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<CourseSectionResponseDto> Sections);

public sealed record CourseSectionResponseDto(
    Guid Id,
    Guid CourseId,
    string Title,
    int SortOrder,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<CourseLessonResponseDto> Lessons,
    List<CourseQuizResponseDto> Quizzes);

public sealed record CourseLessonResponseDto(
    Guid Id,
    Guid CourseSectionId,
    string Title,
    string? VideoUrl,
    int DurationInSeconds,
    int SortOrder,
    bool IsFreePreview,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<LessonResourceResponseDto> Resources);

public sealed record LessonResourceResponseDto(
    Guid Id,
    Guid CourseLessonId,
    string Title,
    string FileUrl,
    string ResourceType,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record CourseQuizResponseDto(
    Guid Id,
    Guid CourseSectionId,
    string Title,
    int PassingScore,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
