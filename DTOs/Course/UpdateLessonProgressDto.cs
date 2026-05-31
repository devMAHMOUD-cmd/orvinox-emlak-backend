using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Course;

public sealed record UpdateLessonProgressDto
{
    [Required]
    public Guid CourseLessonId { get; init; }

    public bool IsCompleted { get; init; }

    [Range(0, int.MaxValue)]
    public int WatchedSeconds { get; init; }
}
