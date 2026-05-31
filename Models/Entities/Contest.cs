using System;
using System.Collections.Generic;

namespace CraftoraApi.Models.Entities;

public partial class Contest
{
    public Guid Id { get; set; }

    public string Title { get; set; } = null!;

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    public string? PrizePool { get; set; }

    public bool? IsActive { get; set; }

    public Guid? CreatedBy { get; set; }

    public virtual ICollection<ContestResult> ContestResults { get; set; } = new List<ContestResult>();

    public virtual User? CreatedByNavigation { get; set; }
}
