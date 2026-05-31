namespace CraftoraApi.Models.Entities;

public partial class Course : BaseEntity
{
    public Guid ProductId { get; set; }

    public string Level { get; set; } = null!;

    public int TotalDurationInMinutes { get; set; }

    public bool IsCertificateIncluded { get; set; }

    public virtual Product Product { get; set; } = null!;

    public virtual ICollection<CourseSection> CourseSections { get; set; } = new List<CourseSection>();
}
