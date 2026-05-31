using System;
using System.Collections.Generic;

namespace CraftoraApi.Models.Entities;

public partial class Coupon
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public Guid ShopId { get; set; }

    public string Code { get; set; } = null!;

    public string DiscountType { get; set; } = null!;

    public decimal DiscountValue { get; set; }

    public decimal? MinimumCartAmount { get; set; }

    public int? MaxUses { get; set; }

    public int? UsedCount { get; set; }

    public DateTime? StartsAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<CouponUse> CouponUses { get; set; } = new List<CouponUse>();

    public virtual Product Product { get; set; } = null!;

    public virtual Shop Shop { get; set; } = null!;
}
