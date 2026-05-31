namespace CraftoraApi.Models.Entities;

public partial class CourseLesson : BaseEntity
{
    public Guid CourseSectionId { get; set; }

    public string Title { get; set; } = null!;

    public string? VideoUrl { get; set; }

    public int DurationInSeconds { get; set; }

    public int SortOrder { get; set; }

    public bool IsFreePreview { get; set; }

    public bool IsActive { get; set; }

    public virtual CourseSection CourseSection { get; set; } = null!;

    public virtual ICollection<LessonResource> LessonResources { get; set; } = new List<LessonResource>();

    public virtual ICollection<LessonProgress> LessonProgresses { get; set; } = new List<LessonProgress>();

    public virtual ICollection<UserLessonProgress> UserLessonProgresses { get; set; } = new List<UserLessonProgress>();
}
