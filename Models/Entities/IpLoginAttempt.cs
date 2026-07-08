using System;

namespace CraftoraApi.Models.Entities;

public partial class IpLoginAttempt
{
    public string IpAddress { get; set; } = null!;

    public int? AttemptCount { get; set; }

    public DateTime? LastAttemptAt { get; set; }

    public DateTime? LockedUntil { get; set; }
}
