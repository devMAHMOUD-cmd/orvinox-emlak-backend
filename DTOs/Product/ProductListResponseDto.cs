namespace CraftoraApi.DTOs.Product;

public sealed record ProductListResponseDto(
    int TotalCount,
    List<ProductResponseDto> Items);
