using System;
using System.Collections.Generic;

namespace CraftoraApi.Models.Entities;

public partial class ContestResult
{
    public Guid Id { get; set; }

    public Guid ContestId { get; set; }

    public Guid UserId { get; set; }

    public int? FinalRank { get; set; }

    public decimal? TotalScore { get; set; }

    public bool? RewardClaimed { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Contest Contest { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
