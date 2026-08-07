using CraftoraApi.DTOs.Common;

namespace CraftoraApi.DTOs.Analytics;

public sealed record CourseAnalyticsDto(
    Guid CourseId,
    Guid ProductId,
    string Title,
    string Level,
    int Views,
    int Sales,
    decimal Revenue,
    IReadOnlyList<CurrencyAmountDto> RevenueByCurrency,
    int TotalLessons,
    int StartedStudents,
    int CompletedStudents,
    double AverageCompletionRate);

public sealed record CourseAnalyticsDetailDto(
    Guid CourseId,
    Guid ProductId,
    string Title,
    string Level,
    int Views,
    int Sales,
    decimal Revenue,
    IReadOnlyList<CurrencyAmountDto> RevenueByCurrency,
    int TotalLessons,
    int StartedStudents,
    int CompletedStudents,
    double AverageCompletionRate,
    IReadOnlyList<CourseLessonAnalyticsDto> Lessons);

public sealed record CourseLessonAnalyticsDto(
    Guid LessonId,
    string Title,
    int SortOrder,
    int StartedStudents,
    int CompletedStudents,
    double CompletionRate);
