using CraftoraApi.DTOs.Order;

namespace CraftoraApi.Services.Interfaces;

public interface IOrderService
{
    Task<List<OrderResponseDto>> CheckoutCartAsync(Guid buyerId, CheckoutRequestDto request);

    Task<List<OrderResponseDto>> GetMyOrdersAsync(Guid buyerId);
}
