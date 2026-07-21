namespace CraftoraApi.DTOs.Shop;

public sealed record FollowedShopListResponseDto(
    IReadOnlyList<PublicShopResponseDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
