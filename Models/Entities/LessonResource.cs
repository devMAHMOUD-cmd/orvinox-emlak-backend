namespace CraftoraApi.Models.Entities;

public partial class LessonResource : BaseEntity
{
    public Guid CourseLessonId { get; set; }

    public string Title { get; set; } = null!;

    public string FileUrl { get; set; } = null!;

    public string ResourceType { get; set; } = null!;

    public virtual CourseLesson CourseLesson { get; set; } = null!;
}
