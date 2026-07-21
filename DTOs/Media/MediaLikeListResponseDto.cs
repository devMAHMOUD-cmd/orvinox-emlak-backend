namespace CraftoraApi.DTOs.Media;

public sealed record MediaLikeUserDto(
    Guid UserId,
    string? FullName,
    string? AvatarPublicUrl,
    Guid? ShopId,
    string? ShopName,
    string? ShopSlug,
    string? ShopLogoPublicUrl,
    bool IsShopVerified,
    DateTime? LikedAt);

public sealed record MediaLikeListResponseDto(
    IReadOnlyList<MediaLikeUserDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
