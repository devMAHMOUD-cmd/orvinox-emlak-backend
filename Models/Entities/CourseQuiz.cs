namespace CraftoraApi.Models.Entities;

public partial class CourseQuiz : BaseEntity
{
    public Guid CourseSectionId { get; set; }

    public string Title { get; set; } = null!;

    public int PassingScore { get; set; }

    public bool IsActive { get; set; }

    public virtual CourseSection CourseSection { get; set; } = null!;
}
