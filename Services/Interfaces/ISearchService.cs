using CraftoraApi.DTOs.Search;
using CraftoraApi.Models.Elasticsearch;

namespace CraftoraApi.Services.Interfaces;

public interface ISearchService
{
    Task IndexProductAsync(ProductDocument product, CancellationToken cancellationToken = default);

    Task DeleteProductIndexAsync(Guid productId, CancellationToken cancellationToken = default);

    Task<int> ReindexProductsAsync(CancellationToken cancellationToken = default);

    Task<SearchResponseDto> SearchProductsAsync(
        SearchRequestDto request,
        CancellationToken cancellationToken = default);
}
