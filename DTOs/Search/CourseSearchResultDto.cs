namespace CraftoraApi.DTOs.Search;

public sealed record CourseSearchResultDto(
    Guid Id,
    string Title,
    string? Description,
    decimal Price,
    string Currency,
    string? CoverImagePublicUrl,
    Guid ShopId,
    string? ShopName,
    string? ShopSlug,
    string? Level,
    int? TotalDurationInMinutes);
