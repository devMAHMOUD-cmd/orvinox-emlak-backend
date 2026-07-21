namespace CraftoraApi.DTOs.Shop;

public sealed record PublicShopResponseDto(
    Guid Id,
    string ShopName,
    string Slug,
    string? ShortDescription,
    string? Description,
    string? LogoUrl,
    string? LogoPublicUrl,
    string? BannerUrl,
    string? BannerPublicUrl,
    string? ExternalUrl,
    string? SocialLinks,
    int FollowerCount,
    int ProductCount,
    decimal? Rating,
    bool IsVerified,
    bool IsFollowedByCurrentUser = false);
