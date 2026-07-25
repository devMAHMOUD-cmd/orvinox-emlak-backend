namespace CraftoraApi.DTOs.Subscription;

public sealed record SubscriptionResponseDto(
    Guid Id,
    Guid ShopId,
    Guid PlanId,
    string PlanCode,
    string PlanName,
    decimal CommissionRate,
    decimal CommissionPercent,
    string? ProviderSubscriptionId,
    string Status,
    DateTime CurrentPeriodEnd,
    DateTime? GracePeriodEnd,
    decimal? Amount,
    string? Currency,
    string? PaymentProvider);
