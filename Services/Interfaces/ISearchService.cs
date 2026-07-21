using CraftoraApi.DTOs.Search;
using CraftoraApi.Models.Elasticsearch;

namespace CraftoraApi.Services.Interfaces;

public interface ISearchService
{
    Task IndexProductAsync(ProductDocument product, CancellationToken cancellationToken = default);

    Task DeleteProductIndexAsync(Guid productId, CancellationToken cancellationToken = default);

    Task<int> ReindexProductsAsync(CancellationToken cancellationToken = default);

    Task IndexShopAsync(ShopDocument shop, CancellationToken cancellationToken = default);

    Task DeleteShopIndexAsync(Guid shopId, CancellationToken cancellationToken = default);

    Task<int> ReindexShopsAsync(CancellationToken cancellationToken = default);

    Task IndexMediaAsync(MediaDocument media, CancellationToken cancellationToken = default);

    Task DeleteMediaIndexAsync(Guid mediaId, CancellationToken cancellationToken = default);

    Task<int> ReindexMediaAsync(CancellationToken cancellationToken = default);

    Task<SearchResponseDto> SearchProductsAsync(
        SearchRequestDto request,
        CancellationToken cancellationToken = default);

    Task<GlobalSearchResponseDto> SearchGlobalAsync(
        GlobalSearchRequestDto request,
        Guid? currentUserId = null,
        CancellationToken cancellationToken = default);
}
