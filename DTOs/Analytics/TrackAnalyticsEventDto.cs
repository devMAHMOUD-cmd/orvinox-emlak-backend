using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace CraftoraApi.DTOs.Analytics;

public sealed record TrackAnalyticsEventDto(
    [property: Required(ErrorMessage = "Analytics eventType zorunludur.")]
    [property: StringLength(50, ErrorMessage = "Analytics eventType en fazla 50 karakter olabilir.")]
    string EventType,
    Guid? ShopId,
    Guid? ProductId,
    Guid? MediaId,
    Guid? OrderId,

    [property: StringLength(100, ErrorMessage = "SessionId en fazla 100 karakter olabilir.")]
    string? SessionId,

    [property: StringLength(100, ErrorMessage = "Source en fazla 100 karakter olabilir.")]
    string? Source,

    [property: StringLength(2000, ErrorMessage = "Referrer en fazla 2000 karakter olabilir.")]
    string? Referrer,

    [property: StringLength(100, ErrorMessage = "UtmSource en fazla 100 karakter olabilir.")]
    string? UtmSource,

    [property: StringLength(100, ErrorMessage = "UtmMedium en fazla 100 karakter olabilir.")]
    string? UtmMedium,

    [property: StringLength(150, ErrorMessage = "UtmCampaign en fazla 150 karakter olabilir.")]
    string? UtmCampaign,

    [property: StringLength(30, ErrorMessage = "DeviceType en fazla 30 karakter olabilir.")]
    string? DeviceType,

    Dictionary<string, JsonElement>? Metadata);
