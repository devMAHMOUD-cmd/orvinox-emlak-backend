namespace CraftoraApi.Models.Entities;

public partial class UserLessonProgress : BaseEntity
{
    public Guid UserId { get; set; }

    public Guid CourseLessonId { get; set; }

    public bool IsCompleted { get; set; }

    public int WatchedSeconds { get; set; }

    public virtual CourseLesson CourseLesson { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
