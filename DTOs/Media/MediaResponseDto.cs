namespace CraftoraApi.DTOs.Media;

public sealed record MediaResponseDto(
    Guid Id,
    Guid ShopId,
    string ShopName,
    string? ShopLogoUrl,
    bool IsShopVerified,
    Guid? ProductId,
    string VideoUrl,
    string? ThumbnailUrl,
    string? Caption,
    int ViewCount,
    int LikeCount,
    int CommentCount,
    string Status,
    DateTime? CreatedAt,
    bool IsLiked = false,
    bool IsSaved = false);
