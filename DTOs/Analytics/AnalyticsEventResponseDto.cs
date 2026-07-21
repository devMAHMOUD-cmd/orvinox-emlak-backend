namespace CraftoraApi.DTOs.Analytics;

public sealed record AnalyticsEventResponseDto(
    Guid Id,
    Guid ShopId,
    Guid? ProductId,
    Guid? MediaId,
    Guid? UserId,
    Guid? OrderId,
    string EventType,
    DateTime? CreatedAt);
