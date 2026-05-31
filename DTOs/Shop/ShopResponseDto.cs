namespace CraftoraApi.DTOs.Shop;

public sealed record ShopResponseDto(
    Guid Id,
    Guid? UserId,
    string ShopName,
    string Slug,
    string? ShortDescription,
    string? Description,
    string? LogoUrl,
    string? BannerUrl,
    int? FollowerCount,
    decimal? Rating,
    bool? IsVerified,
    bool? IsActive,
    bool? HasActiveSubscription,
    DateTime? CreatedAt);
