using CraftoraApi.DTOs.Shop;
using CraftoraApi.Models.Entities;

namespace CraftoraApi.Services.Interfaces;

public interface IShopService
{
    Task<ShopResponseDto> CreateShopAsync(Guid userId, CreateShopDto dto);

    Task<Shop> PrepareNewShopAsync(Guid userId, CreateShopDto dto);

    Task<ShopResponseDto> UpdateShopAsync(Guid userId, UpdateShopDto dto);

    Task<ShopResponseDto> GetMyShopAsync(Guid userId);

    Task<ShopFollowerListResponseDto> GetMyShopFollowersAsync(Guid userId, int page = 1, int pageSize = 30);

    Task<FollowedShopListResponseDto> GetFollowedShopsAsync(Guid userId, int page = 1, int pageSize = 20);

    Task<PublicShopResponseDto> GetShopBySlugAsync(string slug, Guid? currentUserId = null);

    Task<PublicShopResponseDto> GetPublicShopByIdAsync(Guid shopId, Guid? currentUserId = null);

    Task<ShopFollowResponseDto> ToggleFollowAsync(Guid shopId, Guid userId);

    Task<ShopTrafficReportDto> GetShopTrafficReportAsync(
        Guid shopId,
        Guid userId,
        DateTime startDate,
        DateTime endDate);

    Task DeleteShopAsync(Guid shopId, Guid userId);
}
