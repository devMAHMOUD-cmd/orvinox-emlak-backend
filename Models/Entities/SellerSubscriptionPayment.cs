namespace CraftoraApi.Models.Entities;

public sealed class SellerSubscriptionPayment
{
    public Guid Id { get; set; }
    public Guid SubscriptionId { get; set; }
    public Guid ShopId { get; set; }
    public string PaymentProvider { get; set; } = null!;
    public string? ProviderTransactionId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime? BillingPeriodStart { get; set; }
    public DateTime? BillingPeriodEnd { get; set; }
    public DateTime CreatedAt { get; set; }
}
