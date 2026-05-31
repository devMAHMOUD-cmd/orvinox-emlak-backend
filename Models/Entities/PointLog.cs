using System;
using System.Collections.Generic;

namespace CraftoraApi.Models.Entities;

public partial class PointLog
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string ActionType { get; set; } = null!;

    public decimal PointsEarned { get; set; }

    public Guid? ReferenceId { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
