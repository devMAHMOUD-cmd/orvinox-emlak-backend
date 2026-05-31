namespace CraftoraApi.Models.Entities;

public partial class CourseSection : BaseEntity
{
    public Guid CourseId { get; set; }

    public string Title { get; set; } = null!;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; }

    public virtual Course Course { get; set; } = null!;

    public virtual ICollection<CourseLesson> CourseLessons { get; set; } = new List<CourseLesson>();

    public virtual ICollection<CourseQuiz> CourseQuizzes { get; set; } = new List<CourseQuiz>();
}
