namespace CraftoraApi.DTOs.Search;

public sealed record CourseSearchResultDto(
    Guid Id,
    string Title,
    string? Description,
    decimal Price,
    string? CoverImagePublicUrl,
    Guid ShopId,
    string? ShopName,
    string? Level,
    int? TotalDurationInMinutes);
