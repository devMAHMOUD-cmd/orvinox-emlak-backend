namespace CraftoraApi.DTOs.Search;

public sealed record ProductSearchResultDto(
    Guid Id,
    string Title,
    string? Description,
    decimal Price,
    string? CoverImagePublicUrl,
    Guid ShopId,
    string? ShopName);
