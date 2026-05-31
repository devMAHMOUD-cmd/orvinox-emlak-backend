using System;
using System.Collections.Generic;
using CraftoraApi.Models.Enums;

namespace CraftoraApi.Models.Entities;

public partial class SellerSubscription
{
    public Guid Id { get; set; }

    public Guid ShopId { get; set; }

    public string? ProviderSubscriptionId { get; set; }

    public SubStatus Status { get; set; }

    public DateTime CurrentPeriodEnd { get; set; }

    public DateTime? GracePeriodEnd { get; set; }

    public decimal? Amount { get; set; }

    public string? Currency { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public string? PaymentProvider { get; set; }

    public virtual Shop Shop { get; set; } = null!;
}
