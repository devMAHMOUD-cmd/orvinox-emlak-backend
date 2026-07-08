namespace CraftoraApi.DTOs.Order;

public sealed record SellerOrderListResponseDto(
    IReadOnlyList<SellerOrderListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
