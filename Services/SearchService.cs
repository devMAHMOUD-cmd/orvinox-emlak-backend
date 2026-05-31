using CraftoraApi.DTOs.Search;
using CraftoraApi.Models.Elasticsearch;
using CraftoraApi.Services.Interfaces;
using Elastic.Clients.Elasticsearch;

namespace CraftoraApi.Services;

public sealed class SearchService : ISearchService
{
    private const string ProductIndex = "products";

    private readonly ElasticsearchClient _client;

    public SearchService(ElasticsearchClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task IndexProductAsync(
        ProductDocument product,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        await _client.IndexAsync(
            product,
            descriptor => descriptor
                .Index(ProductIndex)
                .Id(product.Id),
            cancellationToken);
    }

    public async Task DeleteProductIndexAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        await _client.DeleteAsync<ProductDocument>(
            productId,
            descriptor => descriptor.Index(ProductIndex),
            cancellationToken);
    }

    public async Task<SearchResponseDto> SearchProductsAsync(
        SearchRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var from = (page - 1) * pageSize;

        var response = await _client.SearchAsync<ProductDocument>(
            descriptor => descriptor
                .Indices(ProductIndex)
                .From(from)
                .Size(pageSize)
                .Query(query => query.Bool(boolQuery => boolQuery
                    .Must(must =>
                    {
                        if (string.IsNullOrWhiteSpace(request.Query))
                        {
                            must.MatchAll();
                            return;
                        }

                        must.MultiMatch(multiMatch => multiMatch
                            .Fields(new[] { "name", "description" })
                            .Query(request.Query)
                            .Fuzziness(new Fuzziness("AUTO")));
                    })
                    .Filter(
                        filter =>
                        {
                            filter.Term(term => term.Field("isActive").Value(true));
                        },
                        filter =>
                        {
                            if (request.CategoryId.HasValue)
                            {
                                filter.Term(term => term
                                    .Field("categoryId")
                                    .Value(request.CategoryId.Value.ToString()));
                                return;
                            }

                            filter.MatchAll();
                        },
                        filter =>
                        {
                            if (request.MinPrice.HasValue || request.MaxPrice.HasValue)
                            {
                                filter.Range(range => range.Number(number => number
                                .Field("price")
                                .Gte(request.MinPrice.HasValue ? (double?)request.MinPrice.Value : null)
                                .Lte(request.MaxPrice.HasValue ? (double?)request.MaxPrice.Value : null)));
                                return;
                            }

                            filter.MatchAll();
                        }))),
            cancellationToken);

        return new SearchResponseDto(
            TotalCount: response.Total,
            Items: response.Documents.ToList());
    }
}
