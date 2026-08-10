namespace CraftoraApi.Infrastructure.Services;

public sealed record VideoProcessingResult(
    string OptimizedVideoUrl,
    string? ThumbnailUrl,
    string? HlsUrl = null,
    int? DurationSeconds = null);
