namespace CraftoraApi.Models.Entities;

public sealed class SellerSubscriptionPlan
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public decimal MonthlyAmount { get; set; }

    public string Currency { get; set; } = null!;

    public decimal CommissionRate { get; set; }

    public List<string> Features { get; set; } = [];

    public int SortOrder { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<SellerSubscription> Subscriptions { get; set; } = [];
}
