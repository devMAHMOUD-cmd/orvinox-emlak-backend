using CraftoraApi.DTOs.Cart;

namespace CraftoraApi.Services.Interfaces;

public interface ICartService
{
    Task<CartResponseDto> AddToCartAsync(Guid userId, AddToCartDto dto);

    Task<CartResponseDto> UpdateCartItemQuantityAsync(Guid userId, Guid cartItemId, int quantity);

    Task RemoveFromCartAsync(Guid userId, Guid cartItemId);

    Task<CartResponseDto> GetUserCartAsync(Guid userId);

    Task ClearCartAsync(Guid userId);
}
