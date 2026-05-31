namespace CraftoraApi.DTOs.Course;

public sealed record CourseProgressResponseDto(
    Guid CourseId,
    int TotalLessons,
    int CompletedLessons,
    double CompletionPercentage);
