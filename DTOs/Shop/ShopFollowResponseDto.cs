namespace CraftoraApi.DTOs.Shop;

public sealed record ShopFollowResponseDto(
    Guid ShopId,
    bool IsFollowing,
    int FollowerCount);
