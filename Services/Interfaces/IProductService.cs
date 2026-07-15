using CraftoraApi.DTOs.Product;
using CraftoraApi.Models.Enums;

namespace CraftoraApi.Services.Interfaces;

public interface IProductService
{
    Task<ProductResponseDto> CreateProductAsync(Guid shopId, CreateProductDto dto);

    Task<ProductResponseDto> GetProductByIdAsync(Guid productId, Guid? currentUserId);

    Task<ProductDownloadUrlResponseDto> GenerateProductDownloadUrlAsync(Guid userId, Guid productId);

    Task<ProductListResponseDto> GetFilteredProductsAsync(
        Guid? categoryId,
        Guid? shopId,
        ProductStatus? status,
        bool includeAllStatuses,
        bool includeInactiveShopProducts,
        int pageNumber,
        int pageSize);

    Task<ProductResponseDto> UpdateProductAsync(Guid productId, Guid shopId, UpdateProductDto dto);

    Task SoftDeleteProductAsync(Guid productId, Guid shopId);
}
