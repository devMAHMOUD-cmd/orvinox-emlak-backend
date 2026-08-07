namespace CraftoraApi.DTOs.Search;

public sealed record ProductSearchResultDto(
    Guid Id,
    string Title,
    string? Description,
    decimal Price,
    string Currency,
    string? CoverImagePublicUrl,
    Guid ShopId,
    string? ShopName,
    string? ShopSlug);
