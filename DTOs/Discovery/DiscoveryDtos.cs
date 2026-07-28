using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace CraftoraApi.DTOs.Discovery;

public sealed record DiscoveryEventBatchRequestDto(
    [property: Required]
    [property: MinLength(1)]
    [property: MaxLength(50)]
    IReadOnlyList<DiscoveryEventRequestDto> Events);

public sealed record DiscoveryEventRequestDto(
    Guid EventId,

    [property: Required]
    [property: StringLength(30)]
    string EventType,

    [property: Required]
    [property: StringLength(4096)]
    string TrackingToken,

    [property: Range(0, 21_600_000)]
    int? DwellMs,

    [property: Range(typeof(decimal), "0", "1")]
    decimal? CompletionRate,

    [property: Range(0, 100)]
    int? VisiblePercentage,

    Dictionary<string, JsonElement>? Metadata);

public sealed record DiscoveryEventBatchResponseDto(
    int AcceptedCount,
    int DuplicateCount,
    int IgnoredCount);

public sealed record DiscoveryFeedbackRequestDto(
    Guid EventId,

    [property: Required]
    [property: StringLength(30)]
    string FeedbackType,

    [property: Required]
    [property: StringLength(4096)]
    string TrackingToken);

public sealed record DiscoveryFeedbackResponseDto(
    Guid Id,
    string FeedbackType,
    string? ContentType,
    Guid? ContentId,
    Guid? ShopId,
    DateTime? ExpiresAt,
    DateTime CreatedAt);
