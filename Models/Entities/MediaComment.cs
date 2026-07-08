using System;
using System.Collections.Generic;

namespace CraftoraApi.Models.Entities;

public partial class MediaComment
{
    public Guid Id { get; set; }

    public Guid MediaId { get; set; }

    public Guid UserId { get; set; }

    public Guid? ParentCommentId { get; set; }

    public string CommentText { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Medium Media { get; set; } = null!;

    public virtual MediaComment? ParentComment { get; set; }

    public virtual ICollection<MediaComment> Replies { get; set; } = new List<MediaComment>();

    public virtual User User { get; set; } = null!;
}
