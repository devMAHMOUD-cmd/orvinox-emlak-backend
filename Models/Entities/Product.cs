using System;
using System.Collections.Generic;
using CraftoraApi.Models.Enums;

namespace CraftoraApi.Models.Entities;

public partial class Product
{
    public Guid Id { get; set; }

    public Guid ShopId { get; set; }

    public Guid CategoryId { get; set; }

    public ProductType Type { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public string? Metadata { get; set; }

    public decimal Price { get; set; }

    public decimal? OriginalPrice { get; set; }

    public string? Currency { get; set; }

    public string? CoverImageUrl { get; set; }

    public string? PreviewVideoUrl { get; set; }

    public string? FileUrl { get; set; }

    public decimal? RatingAverage { get; set; }

    public int? ReviewCount { get; set; }

    public int? SalesCount { get; set; }

    public bool? IsActive { get; set; }

    public bool? IsFeatured { get; set; }

    public ProductStatus Status { get; set; } = ProductStatus.Draft;

    public List<string> Tags { get; set; } = new();

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public decimal? DiscountPrice { get; set; }

    public DateTime? DiscountEndsAt { get; set; }

    public virtual ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();

    public virtual ICollection<Coupon> Coupons { get; set; } = new List<Coupon>();

    public virtual ICollection<Course> Courses { get; set; } = new List<Course>();

    public virtual ICollection<Medium> Media { get; set; } = new List<Medium>();

    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();

    public virtual ICollection<ProductQa> ProductQas { get; set; } = new List<ProductQa>();

    public virtual ICollection<Review> Reviews { get; set; } = new List<Review>();

    public virtual Shop Shop { get; set; } = null!;

    public virtual Category Category { get; set; } = null!;

    public virtual ICollection<UserLibrary> UserLibraries { get; set; } = new List<UserLibrary>();
}
