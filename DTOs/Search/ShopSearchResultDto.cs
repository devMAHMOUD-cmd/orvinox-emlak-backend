namespace CraftoraApi.DTOs.Search;

public sealed record ShopSearchResultDto(
    Guid Id,
    string ShopName,
    string Slug,
    string? ShortDescription,
    string? LogoPublicUrl,
    string? BannerPublicUrl,
    bool? IsVerified);
