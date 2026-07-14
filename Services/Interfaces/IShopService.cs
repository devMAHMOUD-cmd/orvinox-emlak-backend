using CraftoraApi.DTOs.Shop;

namespace CraftoraApi.Services.Interfaces;

public interface IShopService
{
    Task<ShopResponseDto> CreateShopAsync(Guid userId, CreateShopDto dto);

    Task<ShopResponseDto> UpdateShopAsync(Guid userId, UpdateShopDto dto);

    Task<ShopResponseDto> GetMyShopAsync(Guid userId);

    Task<PublicShopResponseDto> GetShopBySlugAsync(string slug);

    Task ToggleFollowAsync(Guid shopId, Guid userId);

    Task<ShopTrafficReportDto> GetShopTrafficReportAsync(
        Guid shopId,
        Guid userId,
        DateTime startDate,
        DateTime endDate);

    Task DeleteShopAsync(Guid shopId, Guid userId);
}
