using System;
using System.Collections.Generic;
using System.Net;

namespace CraftoraApi.Models.Entities;

public partial class ShopVisit
{
    public Guid Id { get; set; }

    public Guid ShopId { get; set; }

    public Guid? UserId { get; set; }

    public IPAddress? IpAddress { get; set; }

    public DateTime? VisitedAt { get; set; }

    public virtual Shop Shop { get; set; } = null!;

    public virtual User? User { get; set; }
}
