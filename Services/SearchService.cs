using CraftoraApi.Data;
using CraftoraApi.DTOs.Search;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Elasticsearch;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Elastic.Clients.Elasticsearch;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class SearchService : ISearchService
{
    private const string ProductIndex = "products";

    private readonly ElasticsearchClient _client;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<SearchService> _logger;

    public SearchService(
        ElasticsearchClient client,
        AppDbContext dbContext,
        ILogger<SearchService> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task IndexProductAsync(
        ProductDocument product,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (!product.IsActive || !product.IsPublished || !product.ShopIsActive)
        {
            await DeleteProductIndexAsync(product.Id, cancellationToken);
            return;
        }

        var response = await _client.IndexAsync(
            product,
            descriptor => descriptor
                .Index(ProductIndex)
                .Id(product.Id),
            cancellationToken);

        if (!response.IsValidResponse)
        {
            _logger.LogError(
                "Elasticsearch product indexing failed for {ProductId}. HTTP status: {HttpStatusCode}. Details: {DebugInformation}",
                product.Id,
                response.ApiCallDetails?.HttpStatusCode,
                response.DebugInformation);
            throw new ExternalServiceException("Elasticsearch", "Urun arama indexine yazilamadi.");
        }
    }

    public async Task DeleteProductIndexAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.DeleteAsync<ProductDocument>(
            productId,
            descriptor => descriptor.Index(ProductIndex),
            cancellationToken);

        var httpStatusCode = response.ApiCallDetails?.HttpStatusCode;
        if (!response.IsValidResponse && httpStatusCode != 404)
        {
            _logger.LogError(
                "Elasticsearch product index deletion failed for {ProductId}. HTTP status: {HttpStatusCode}. Details: {DebugInformation}",
                productId,
                httpStatusCode,
                response.DebugInformation);
            throw new ExternalServiceException("Elasticsearch", "Urun arama indexinden silinemedi.");
        }
    }

    public async Task<int> ReindexProductsAsync(CancellationToken cancellationToken = default)
    {
        var documents = await _dbContext.Products
            .AsNoTracking()
            .Where(product =>
                product.IsActive == true &&
                product.Status == ProductStatus.Published &&
                product.Shop.IsActive == true)
            .Select(product => new ProductDocument
            {
                Id = product.Id,
                Name = product.Title,
                Description = product.Description,
                Price = product.Price,
                CategoryId = product.CategoryId,
                ShopId = product.ShopId,
                IsActive = true,
                IsPublished = true,
                ShopIsActive = true
            })
            .ToListAsync(cancellationToken);

        var existsResponse = await _client.Indices.ExistsAsync(ProductIndex, cancellationToken);
        var existsStatusCode = existsResponse.ApiCallDetails?.HttpStatusCode;
        if (!existsResponse.IsValidResponse && existsStatusCode != 404)
        {
            _logger.LogError(
                "Elasticsearch product index existence check failed. HTTP status: {HttpStatusCode}. Details: {DebugInformation}",
                existsStatusCode,
                existsResponse.DebugInformation);
            throw new ExternalServiceException("Elasticsearch", "Urun arama indexi kontrol edilemedi.");
        }

        if (existsResponse.Exists)
        {
            var deleteResponse = await _client.Indices.DeleteAsync(ProductIndex, cancellationToken);
            var deleteStatusCode = deleteResponse.ApiCallDetails?.HttpStatusCode;
            if (!deleteResponse.IsValidResponse && deleteStatusCode != 404)
            {
                _logger.LogError(
                    "Elasticsearch product index cleanup failed. HTTP status: {HttpStatusCode}. Details: {DebugInformation}",
                    deleteStatusCode,
                    deleteResponse.DebugInformation);
                throw new ExternalServiceException("Elasticsearch", "Urun arama indexi temizlenemedi.");
            }
        }

        var createResponse = await _client.Indices.CreateAsync(ProductIndex, cancellationToken);
        if (!createResponse.IsValidResponse)
        {
            _logger.LogError(
                "Elasticsearch product index creation failed. HTTP status: {HttpStatusCode}. Details: {DebugInformation}",
                createResponse.ApiCallDetails?.HttpStatusCode,
                createResponse.DebugInformation);
            throw new ExternalServiceException("Elasticsearch", "Urun arama indexi olusturulamadi.");
        }

        if (documents.Count > 0)
        {
            var bulkResponse = await _client.IndexManyAsync(
                documents,
                ProductIndex,
                cancellationToken);
            if (!bulkResponse.IsValidResponse || bulkResponse.Errors)
            {
                _logger.LogError(
                    "Elasticsearch product bulk indexing failed. HTTP status: {HttpStatusCode}. Has item errors: {HasErrors}. Details: {DebugInformation}",
                    bulkResponse.ApiCallDetails?.HttpStatusCode,
                    bulkResponse.Errors,
                    bulkResponse.DebugInformation);
                throw new ExternalServiceException("Elasticsearch", "Urunler toplu olarak indexlenemedi.");
            }
        }

        var refreshResponse = await _client.Indices.RefreshAsync(ProductIndex, cancellationToken);
        if (!refreshResponse.IsValidResponse)
        {
            _logger.LogError(
                "Elasticsearch product index refresh failed. HTTP status: {HttpStatusCode}. Details: {DebugInformation}",
                refreshResponse.ApiCallDetails?.HttpStatusCode,
                refreshResponse.DebugInformation);
            throw new ExternalServiceException("Elasticsearch", "Urun arama indexi yenilenemedi.");
        }

        return documents.Count;
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
                            must.MatchAll(new Elastic.Clients.Elasticsearch.QueryDsl.MatchAllQuery());
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
                            filter.Term(term => term.Field(product => product.IsActive).Value(true));
                        },
                        filter =>
                        {
                            filter.Term(term => term.Field(product => product.IsPublished).Value(true));
                        },
                        filter =>
                        {
                            filter.Term(term => term.Field(product => product.ShopIsActive).Value(true));
                        },
                        filter =>
                        {
                            if (request.CategoryId.HasValue)
                            {
                                filter.Term(term => term
                                    .Field(product => product.CategoryId)
                                    .Value(request.CategoryId.Value.ToString()));
                                return;
                            }

                            filter.MatchAll(new Elastic.Clients.Elasticsearch.QueryDsl.MatchAllQuery());
                        },
                        filter =>
                        {
                            if (request.MinPrice.HasValue || request.MaxPrice.HasValue)
                            {
                                filter.Range(range => range.NumberRange(number => number
                                    .Field(product => product.Price)
                                    .Gte(request.MinPrice.HasValue ? (double?)request.MinPrice.Value : null)
                                    .Lte(request.MaxPrice.HasValue ? (double?)request.MaxPrice.Value : null)));
                                return;
                            }

                            filter.MatchAll(new Elastic.Clients.Elasticsearch.QueryDsl.MatchAllQuery());
                        }))),
            cancellationToken);

        if (!response.IsValidResponse)
        {
            _logger.LogError(
                "Elasticsearch product search failed. HTTP status: {HttpStatusCode}. Details: {DebugInformation}",
                response.ApiCallDetails?.HttpStatusCode,
                response.DebugInformation);
            return new SearchResponseDto(0, new List<ProductDocument>());
        }

        var documents = response.Documents.ToList();
        if (documents.Count > 0)
        {
            var documentIds = documents.Select(document => document.Id).ToList();
            var accessibleProductIds = await _dbContext.Products
                .AsNoTracking()
                .Where(product =>
                    documentIds.Contains(product.Id) &&
                    product.IsActive == true &&
                    product.Status == ProductStatus.Published &&
                    product.Shop.IsActive == true)
                .Select(product => product.Id)
                .ToHashSetAsync(cancellationToken);

            documents = documents
                .Where(document => accessibleProductIds.Contains(document.Id))
                .ToList();
        }

        return new SearchResponseDto(
            TotalCount: response.Total,
            Items: documents);
    }
}
