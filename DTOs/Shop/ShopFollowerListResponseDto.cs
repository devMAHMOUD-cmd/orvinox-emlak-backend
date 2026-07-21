namespace CraftoraApi.DTOs.Shop;

public sealed record ShopFollowerDto(
    Guid UserId,
    string? FullName,
    string? AvatarPublicUrl,
    Guid? ShopId,
    string? ShopName,
    string? ShopSlug,
    string? ShopLogoPublicUrl,
    bool IsShopVerified,
    DateTime? FollowedAt);

public sealed record ShopFollowerListResponseDto(
    IReadOnlyList<ShopFollowerDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
