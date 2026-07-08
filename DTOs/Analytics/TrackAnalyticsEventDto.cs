using System.Text.Json;

namespace CraftoraApi.DTOs.Analytics;

public sealed record TrackAnalyticsEventDto(
    string EventType,
    Guid? ShopId,
    Guid? ProductId,
    Guid? OrderId,
    string? SessionId,
    string? Source,
    string? Referrer,
    string? UtmSource,
    string? UtmMedium,
    string? UtmCampaign,
    string? DeviceType,
    Dictionary<string, JsonElement>? Metadata);
