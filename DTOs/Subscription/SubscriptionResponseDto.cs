namespace CraftoraApi.DTOs.Subscription;

public sealed record SubscriptionResponseDto(
    Guid Id,
    Guid ShopId,
    string? ProviderSubscriptionId,
    string Status,
    DateTime CurrentPeriodEnd,
    DateTime? GracePeriodEnd,
    decimal? Amount,
    string? Currency,
    string? PaymentProvider);
