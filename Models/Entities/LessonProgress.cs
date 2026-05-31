using System;
using System.Collections.Generic;

namespace CraftoraApi.Models.Entities;

public partial class LessonProgress
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid LessonId { get; set; }

    public bool? IsCompleted { get; set; }

    public int? WatchedSeconds { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual CourseLesson Lesson { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
