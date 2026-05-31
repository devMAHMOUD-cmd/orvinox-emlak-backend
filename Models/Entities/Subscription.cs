using System;
using System.Collections.Generic;

namespace CraftoraApi.Models.Entities;

public partial class Subscription
{
    public Guid Id { get; set; }

    public Guid ShopId { get; set; }

    public Guid UserId { get; set; }

    public bool? WantsNotifications { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Shop Shop { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
