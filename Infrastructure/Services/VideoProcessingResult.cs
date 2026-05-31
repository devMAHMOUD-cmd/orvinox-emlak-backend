namespace CraftoraApi.Infrastructure.Services;

public sealed record VideoProcessingResult(
    string VideoUrl,
    string? ThumbnailUrl);
