using System;
using System.Collections.Generic;

namespace CraftoraApi.Models.Entities;

public partial class Shop
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string ShopName { get; set; } = null!;

    public string Slug { get; set; } = null!;

    public string? ExternalUrl { get; set; }

    public string? ShortDescription { get; set; }

    public string? Description { get; set; }

    public string? AboutContent { get; set; }

    public string? SocialLinks { get; set; }

    public string? LogoUrl { get; set; }

    public string? BannerUrl { get; set; }

    public int? FollowerCount { get; set; }

    public decimal? Rating { get; set; }

    public bool? IsVerified { get; set; }

    public bool? IsActive { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<Coupon> Coupons { get; set; } = new List<Coupon>();

    public virtual ICollection<Medium> Media { get; set; } = new List<Medium>();

    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();

    public virtual ICollection<Product> Products { get; set; } = new List<Product>();

    public virtual SellerSubscription? SellerSubscription { get; set; }

    public virtual ICollection<ShopVisit> ShopVisits { get; set; } = new List<ShopVisit>();

    public virtual ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();

    public virtual User User { get; set; } = null!;
}
