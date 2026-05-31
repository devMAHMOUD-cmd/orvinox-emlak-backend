using System;
using System.Collections.Generic;

namespace CraftoraApi.Models.Entities;

public partial class MediaWatchHistory
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid MediaId { get; set; }

    public DateTime? WatchedAt { get; set; }

    public bool? IsPointEarned { get; set; }

    public virtual Medium Media { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
