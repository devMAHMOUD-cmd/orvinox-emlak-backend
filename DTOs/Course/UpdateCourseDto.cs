using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Course;

public sealed record UpdateCourseDto
{
    [Required]
    [StringLength(50)]
    public string Level { get; init; } = null!;

    [Range(0, int.MaxValue)]
    public int TotalDurationInMinutes { get; init; }

    public bool IsCertificateIncluded { get; init; }

    public List<UpdateCourseSectionDto> Sections { get; init; } = new();
}

public sealed record UpdateCourseSectionDto
{
    [Required]
    [StringLength(255)]
    public string Title { get; init; } = null!;

    public int SortOrder { get; init; }

    public bool IsActive { get; init; } = true;

    public List<UpdateCourseLessonDto> Lessons { get; init; } = new();

    public List<UpdateCourseQuizDto> Quizzes { get; init; } = new();
}

public sealed record UpdateCourseLessonDto
{
    [Required]
    [StringLength(255)]
    public string Title { get; init; } = null!;

    public string? VideoUrl { get; init; }

    [Range(0, int.MaxValue)]
    public int DurationInSeconds { get; init; }

    public int SortOrder { get; init; }

    public bool IsFreePreview { get; init; }

    public bool IsActive { get; init; } = true;

    public List<UpdateLessonResourceDto> Resources { get; init; } = new();
}

public sealed record UpdateLessonResourceDto
{
    [Required]
    [StringLength(255)]
    public string Title { get; init; } = null!;

    [Required]
    public string FileUrl { get; init; } = null!;

    [Required]
    [StringLength(50)]
    public string ResourceType { get; init; } = null!;
}

public sealed record UpdateCourseQuizDto
{
    [Required]
    [StringLength(255)]
    public string Title { get; init; } = null!;

    [Range(0, 100)]
    public int PassingScore { get; init; }

    public bool IsActive { get; init; } = true;
}
