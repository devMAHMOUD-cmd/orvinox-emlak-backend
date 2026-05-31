using System;
using System.Collections.Generic;

namespace CraftoraApi.Models.Entities;

public partial class UserPoint
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public decimal? TotalPoints { get; set; }

    public int? CurrentRank { get; set; }

    public int? CurrentStreak { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
