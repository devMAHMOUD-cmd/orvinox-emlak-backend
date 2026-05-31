namespace CraftoraApi.DTOs.Cart;

public sealed record CartResponseDto(
    List<CartItemResponseDto> Items,
    decimal TotalPrice);
