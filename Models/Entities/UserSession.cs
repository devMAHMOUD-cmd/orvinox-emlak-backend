using System;
using System.Collections.Generic;
using System.Net;

namespace CraftoraApi.Models.Entities;

public partial class UserSession
{
    public Guid Id { get; set; }

    public Guid? UserId { get; set; }

    public string RefreshToken { get; set; } = null!;

    public string? DeviceId { get; set; }

    public IPAddress? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual User? User { get; set; }
}
