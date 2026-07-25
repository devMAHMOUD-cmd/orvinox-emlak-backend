namespace CraftoraApi.DTOs.Subscription;

public sealed record SubscriptionPlanResponseDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    decimal MonthlyAmount,
    string Currency,
    decimal CommissionRate,
    decimal CommissionPercent,
    IReadOnlyList<string> Features,
    int SortOrder);
