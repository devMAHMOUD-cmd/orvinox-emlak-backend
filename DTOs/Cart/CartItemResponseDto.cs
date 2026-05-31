namespace CraftoraApi.DTOs.Cart;

public sealed record CartItemResponseDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    decimal ProductPrice,
    int Quantity,
    decimal SubTotal);
