using System;
using System.Collections.Generic;
using CraftoraApi.Models.Enums;

namespace CraftoraApi.Models.Entities;

public partial class Order
{
    public Guid Id { get; set; }

    public Guid BuyerId { get; set; }

    public Guid ProductId { get; set; }

    public Guid ShopId { get; set; }

    public string OrderNumber { get; set; } = null!;

    public decimal Amount { get; set; }

    public decimal SubtotalAmount { get; set; }

    public decimal DiscountAmount { get; set; }

    public string? Currency { get; set; }

    public decimal? PlatformFee { get; set; }

    public Guid? SubscriptionPlanId { get; set; }

    public decimal? CommissionRate { get; set; }

    public decimal? SellerEarnings { get; set; }

    public OrderStatus Status { get; set; }

    public string? StripePaymentId { get; set; }

    public string? InvoicePdfUrl { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual User Buyer { get; set; } = null!;

    public virtual ICollection<CouponUse> CouponUses { get; set; } = new List<CouponUse>();

    public virtual Payment? Payment { get; set; }

    public virtual Product Product { get; set; } = null!;

    public virtual Shop Shop { get; set; } = null!;
}
