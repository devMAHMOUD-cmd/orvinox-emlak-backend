namespace CraftoraApi.DTOs.Search;

public sealed record MediaSearchResultDto(
    Guid Id,
    string? Caption,
    List<string> Hashtags,
    string? ThumbnailPublicUrl,
    string? VideoPublicUrl,
    Guid ShopId,
    string ShopName,
    Guid? ProductId,
    string? ProductTitle);
