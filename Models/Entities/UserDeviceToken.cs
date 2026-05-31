using System;
using System.Collections.Generic;

namespace CraftoraApi.Models.Entities;

public partial class UserDeviceToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Token { get; set; } = null!;

    public string DeviceType { get; set; } = null!;

    public string? DeviceId { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
