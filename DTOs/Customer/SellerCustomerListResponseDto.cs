namespace CraftoraApi.DTOs.Customer;

public sealed record SellerCustomerListResponseDto(
    IReadOnlyList<SellerCustomerListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
