using System;
using System.Collections.Generic;

namespace CraftoraApi.Models.Entities;

public partial class UserLibrary
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid ProductId { get; set; }

    public DateTime? PurchasedAt { get; set; }

    public DateTime? LastAccessedAt { get; set; }

    public virtual Product Product { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
