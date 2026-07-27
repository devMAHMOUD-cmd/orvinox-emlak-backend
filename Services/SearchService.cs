using CraftoraApi.Data;
using CraftoraApi.DTOs.Search;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Elasticsearch;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Elastic.Clients.Elasticsearch;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class SearchService : ISearchService
{
    private const string ProductIndex = "products";
    private const string ShopIndex = "shops";
    private const string MediaIndex = "media";
    private const string PublicAssetsBucketName = "public-assets";
    private const string PrivateProductsBucketName = "private-products";
    private const int PublicUrlExpiryMinutes = 60;
    private const int MaxSearchQueryLength = 200;
    private const decimal MaxSearchPrice = 99999999.99m;

    private readonly ElasticsearchClient _client;
    private readonly AppDbContext _dbContext;
    private readonly IStorageService _storageService;
    private readonly ILogger<SearchService> _logger;

    public SearchService(
        ElasticsearchClient client,
        AppDbContext dbContext,
        IStorageService storageService,
        ILogger<SearchService> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
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

    public async Task IndexShopAsync(
        ShopDocument shop,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shop);

        if (!shop.IsActive)
        {
            await DeleteShopIndexAsync(shop.Id, cancellationToken);
            return;
        }

        var response = await _client.IndexAsync(
            shop,
            descriptor => descriptor
                .Index(ShopIndex)
                .Id(shop.Id),
            cancellationToken);

        if (!response.IsValidResponse)
        {
            _logger.LogError(
                "Elasticsearch shop indexing failed for {ShopId}. HTTP status: {HttpStatusCode}. Details: {DebugInformation}",
                shop.Id,
                response.ApiCallDetails?.HttpStatusCode,
                response.DebugInformation);
            throw new ExternalServiceException("Elasticsearch", "Magaza arama indexine yazilamadi.");
        }
    }

    public async Task DeleteShopIndexAsync(
        Guid shopId,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.DeleteAsync<ShopDocument>(
            shopId,
            descriptor => descriptor.Index(ShopIndex),
            cancellationToken);

        var httpStatusCode = response.ApiCallDetails?.HttpStatusCode;
        if (!response.IsValidResponse && httpStatusCode != 404)
        {
            _logger.LogError(
                "Elasticsearch shop index deletion failed for {ShopId}. HTTP status: {HttpStatusCode}. Details: {DebugInformation}",
                shopId,
                httpStatusCode,
                response.DebugInformation);
            throw new ExternalServiceException("Elasticsearch", "Magaza arama indexinden silinemedi.");
        }
    }

    public async Task IndexMediaAsync(
        MediaDocument media,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(media);

        if (!media.IsActive || !media.ShopIsActive)
        {
            await DeleteMediaIndexAsync(media.Id, cancellationToken);
            return;
        }

        var response = await _client.IndexAsync(
            media,
            descriptor => descriptor
                .Index(MediaIndex)
                .Id(media.Id),
            cancellationToken);

        if (!response.IsValidResponse)
        {
            _logger.LogError(
                "Elasticsearch media indexing failed for {MediaId}. HTTP status: {HttpStatusCode}. Details: {DebugInformation}",
                media.Id,
                response.ApiCallDetails?.HttpStatusCode,
                response.DebugInformation);
            throw new ExternalServiceException("Elasticsearch", "Medya arama indexine yazilamadi.");
        }
    }

    public async Task DeleteMediaIndexAsync(
        Guid mediaId,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.DeleteAsync<MediaDocument>(
            mediaId,
            descriptor => descriptor.Index(MediaIndex),
            cancellationToken);

        var httpStatusCode = response.ApiCallDetails?.HttpStatusCode;
        if (!response.IsValidResponse && httpStatusCode != 404)
        {
            _logger.LogError(
                "Elasticsearch media index deletion failed for {MediaId}. HTTP status: {HttpStatusCode}. Details: {DebugInformation}",
                mediaId,
                httpStatusCode,
                response.DebugInformation);
            throw new ExternalServiceException("Elasticsearch", "Medya arama indexinden silinemedi.");
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
                Type = product.Type == ProductType.Course ? "course" : "digital_file",
                Price = product.Price,
                CategoryId = product.CategoryId,
                ShopId = product.ShopId,
                ShopName = product.Shop.ShopName,
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

    public async Task<int> ReindexShopsAsync(CancellationToken cancellationToken = default)
    {
        var documents = await _dbContext.Shops
            .AsNoTracking()
            .Where(shop => shop.IsActive == true)
            .Select(shop => new ShopDocument
            {
                Id = shop.Id,
                ShopName = shop.ShopName,
                Slug = shop.Slug,
                ShortDescription = shop.ShortDescription,
                LogoObjectKey = shop.LogoUrl,
                BannerObjectKey = shop.BannerUrl,
                IsActive = true,
                IsVerified = shop.IsVerified == true,
                FollowerCount = shop.FollowerCount ?? 0
            })
            .ToListAsync(cancellationToken);

        await RecreateIndexAsync(ShopIndex, "shop", cancellationToken);

        if (documents.Count > 0)
        {
            var bulkResponse = await _client.IndexManyAsync(
                documents,
                ShopIndex,
                cancellationToken);
            if (!bulkResponse.IsValidResponse || bulkResponse.Errors)
            {
                _logger.LogError(
                    "Elasticsearch shop bulk indexing failed. HTTP status: {HttpStatusCode}. Has item errors: {HasErrors}. Details: {DebugInformation}",
                    bulkResponse.ApiCallDetails?.HttpStatusCode,
                    bulkResponse.Errors,
                    bulkResponse.DebugInformation);
                throw new ExternalServiceException("Elasticsearch", "Magazalar toplu olarak indexlenemedi.");
            }
        }

        await RefreshIndexAsync(ShopIndex, "shop", cancellationToken);

        return documents.Count;
    }

    public async Task<int> ReindexMediaAsync(CancellationToken cancellationToken = default)
    {
        var media = await _dbContext.Media
            .AsNoTracking()
            .Include(item => item.Shop)
            .Include(item => item.Product)
            .Where(item =>
                item.IsActive == true &&
                item.Shop.IsActive == true)
            .ToListAsync(cancellationToken);

        var documents = media.Select(MapMediaDocument).ToList();

        await RecreateIndexAsync(MediaIndex, "media", cancellationToken);

        if (documents.Count > 0)
        {
            var bulkResponse = await _client.IndexManyAsync(
                documents,
                MediaIndex,
                cancellationToken);
            if (!bulkResponse.IsValidResponse || bulkResponse.Errors)
            {
                _logger.LogError(
                    "Elasticsearch media bulk indexing failed. HTTP status: {HttpStatusCode}. Has item errors: {HasErrors}. Details: {DebugInformation}",
                    bulkResponse.ApiCallDetails?.HttpStatusCode,
                    bulkResponse.Errors,
                    bulkResponse.DebugInformation);
                throw new ExternalServiceException("Elasticsearch", "Medyalar toplu olarak indexlenemedi.");
            }
        }

        await RefreshIndexAsync(MediaIndex, "media", cancellationToken);

        return documents.Count;
    }

    public async Task<SearchResponseDto> SearchProductsAsync(
        SearchRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Query?.Length > MaxSearchQueryLength)
        {
            throw new BadRequestException("Arama metni en fazla 200 karakter olabilir.");
        }

        if (request.MinPrice is < 0 or > MaxSearchPrice ||
            request.MaxPrice is < 0 or > MaxSearchPrice)
        {
            throw new BadRequestException("Fiyat filtresi gecersiz.");
        }

        if (request.MinPrice.HasValue &&
            request.MaxPrice.HasValue &&
            request.MinPrice.Value > request.MaxPrice.Value)
        {
            throw new BadRequestException("Minimum fiyat maksimum fiyattan buyuk olamaz.");
        }

        if (request.Page < 1 || request.PageSize is < 1 or > 100)
        {
            throw new BadRequestException("Arama sayfalama degerleri gecersiz.");
        }

        var page = request.Page;
        var pageSize = request.PageSize;
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

    public async Task<GlobalSearchResponseDto> SearchGlobalAsync(
        GlobalSearchRequestDto request,
        Guid? currentUserId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Query?.Length > MaxSearchQueryLength)
        {
            throw new BadRequestException("Arama metni en fazla 200 karakter olabilir.");
        }

        if (request.Page < 1 || request.PageSize is < 1 or > 50)
        {
            throw new BadRequestException("Global arama sayfalama degerleri gecersiz.");
        }

        var query = request.Query?.Trim() ?? string.Empty;
        var page = request.Page;
        var pageSize = request.PageSize;
        var from = (page - 1) * pageSize;

        var productDocuments = await SearchProductDocumentsForGlobalAsync(
            query,
            "digital_file",
            from,
            pageSize,
            cancellationToken);
        var courseDocuments = await SearchProductDocumentsForGlobalAsync(
            query,
            "course",
            from,
            pageSize,
            cancellationToken);
        var shopDocuments = await SearchShopDocumentsAsync(query, from, pageSize, cancellationToken);
        var mediaDocuments = await SearchMediaDocumentsAsync(query, from, pageSize, cancellationToken);

        var productResults = await MapProductResultsAsync(productDocuments, cancellationToken);
        var courseResults = await MapCourseResultsAsync(courseDocuments, cancellationToken);
        var followedShopIds = currentUserId.HasValue && shopDocuments.Count > 0
            ? await _dbContext.Subscriptions
                .AsNoTracking()
                .Where(subscription =>
                    subscription.UserId == currentUserId.Value &&
                    shopDocuments.Select(shop => shop.Id).Contains(subscription.ShopId))
                .Select(subscription => subscription.ShopId)
                .ToHashSetAsync(cancellationToken)
            : new HashSet<Guid>();

        var shopResults = shopDocuments
            .Select(document => new ShopSearchResultDto(
                Id: document.Id,
                ShopName: document.ShopName,
                Slug: document.Slug,
                ShortDescription: document.ShortDescription,
                LogoPublicUrl: GeneratePublicAssetUrl(document.LogoObjectKey),
                BannerPublicUrl: GeneratePublicAssetUrl(document.BannerObjectKey),
                IsVerified: document.IsVerified,
                IsFollowedByCurrentUser: followedShopIds.Contains(document.Id)))
            .ToList();
        var mediaResults = mediaDocuments
            .Select(document => new MediaSearchResultDto(
                Id: document.Id,
                Caption: document.Caption,
                Hashtags: document.Hashtags,
                ThumbnailPublicUrl: GeneratePublicAssetUrl(document.ThumbnailObjectKey)
                    ?? GeneratePublicAssetUrl(document.ProductCoverImageObjectKey),
                VideoPublicUrl: GeneratePrivateProductUrl(document.VideoObjectKey),
                ShopId: document.ShopId,
                ShopName: document.ShopName,
                ProductId: document.ProductId,
                ProductTitle: document.ProductTitle))
            .ToList();

        return new GlobalSearchResponseDto(
            Query: query,
            Products: productResults,
            Courses: courseResults,
            Media: mediaResults,
            Shops: shopResults);
    }

    private async Task<List<ProductDocument>> SearchProductDocumentsForGlobalAsync(
        string query,
        string productType,
        int from,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var response = await _client.SearchAsync<ProductDocument>(
            descriptor => descriptor
                .Indices(ProductIndex)
                .From(from)
                .Size(pageSize)
                .Query(searchQuery => searchQuery.Bool(boolQuery => boolQuery
                    .Must(must =>
                    {
                        if (string.IsNullOrWhiteSpace(query))
                        {
                            must.MatchAll(new Elastic.Clients.Elasticsearch.QueryDsl.MatchAllQuery());
                            return;
                        }

                        must.MultiMatch(multiMatch => multiMatch
                            .Fields(new[] { "name", "description", "shopName" })
                            .Query(query)
                            .Fuzziness(new Fuzziness("AUTO")));
                    })
                    .Filter(
                        filter => filter.Term(term => term.Field(product => product.IsActive).Value(true)),
                        filter => filter.Term(term => term.Field(product => product.IsPublished).Value(true)),
                        filter => filter.Term(term => term.Field(product => product.ShopIsActive).Value(true)),
                        filter => filter.Term(term => term
                            .Field(product => product.Type.Suffix("keyword"))
                            .Value(productType))))),
            cancellationToken);

        if (!response.IsValidResponse)
        {
            _logger.LogError(
                "Elasticsearch global product search failed. HTTP status: {HttpStatusCode}. Details: {DebugInformation}",
                response.ApiCallDetails?.HttpStatusCode,
                response.DebugInformation);
            return new List<ProductDocument>();
        }

        return response.Documents.ToList();
    }

    private async Task<List<ShopDocument>> SearchShopDocumentsAsync(
        string query,
        int from,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var response = await _client.SearchAsync<ShopDocument>(
            descriptor => descriptor
                .Indices(ShopIndex)
                .From(from)
                .Size(pageSize)
                .Query(searchQuery => searchQuery.Bool(boolQuery => boolQuery
                    .Must(must =>
                    {
                        if (string.IsNullOrWhiteSpace(query))
                        {
                            must.MatchAll(new Elastic.Clients.Elasticsearch.QueryDsl.MatchAllQuery());
                            return;
                        }

                        must.MultiMatch(multiMatch => multiMatch
                            .Fields(new[] { "shopName", "slug", "shortDescription" })
                            .Query(query)
                            .Fuzziness(new Fuzziness("AUTO")));
                    })
                    .Filter(filter => filter.Term(term => term.Field(shop => shop.IsActive).Value(true))))),
            cancellationToken);

        if (!response.IsValidResponse)
        {
            _logger.LogError(
                "Elasticsearch shop search failed. HTTP status: {HttpStatusCode}. Details: {DebugInformation}",
                response.ApiCallDetails?.HttpStatusCode,
                response.DebugInformation);
            return new List<ShopDocument>();
        }

        return response.Documents.ToList();
    }

    private async Task<List<MediaDocument>> SearchMediaDocumentsAsync(
        string query,
        int from,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var response = await _client.SearchAsync<MediaDocument>(
            descriptor => descriptor
                .Indices(MediaIndex)
                .From(from)
                .Size(pageSize)
                .Query(searchQuery => searchQuery.Bool(boolQuery => boolQuery
                    .Must(must =>
                    {
                        if (string.IsNullOrWhiteSpace(query))
                        {
                            must.MatchAll(new Elastic.Clients.Elasticsearch.QueryDsl.MatchAllQuery());
                            return;
                        }

                        must.MultiMatch(multiMatch => multiMatch
                            .Fields(new[] { "caption", "hashtags", "shopName", "productTitle" })
                            .Query(query)
                            .Fuzziness(new Fuzziness("AUTO")));
                    })
                    .Filter(
                        filter => filter.Term(term => term.Field(media => media.IsActive).Value(true)),
                        filter => filter.Term(term => term.Field(media => media.ShopIsActive).Value(true))))),
            cancellationToken);

        if (!response.IsValidResponse)
        {
            _logger.LogError(
                "Elasticsearch media search failed. HTTP status: {HttpStatusCode}. Details: {DebugInformation}",
                response.ApiCallDetails?.HttpStatusCode,
                response.DebugInformation);
            return new List<MediaDocument>();
        }

        return response.Documents.ToList();
    }

    private async Task<List<ProductSearchResultDto>> MapProductResultsAsync(
        List<ProductDocument> documents,
        CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
        {
            return new List<ProductSearchResultDto>();
        }

        var productIds = documents.Select(document => document.Id).ToList();
        var products = await _dbContext.Products
            .AsNoTracking()
            .Include(product => product.Shop)
            .Where(product =>
                productIds.Contains(product.Id) &&
                product.IsActive == true &&
                product.Status == ProductStatus.Published &&
                product.Shop.IsActive == true)
            .ToListAsync(cancellationToken);
        var productMap = products.ToDictionary(product => product.Id);

        return documents
            .Where(document => productMap.ContainsKey(document.Id))
            .Select(document =>
            {
                var product = productMap[document.Id];
                return new ProductSearchResultDto(
                    Id: product.Id,
                    Title: product.Title,
                    Description: product.Description,
                    Price: product.Price,
                    CoverImagePublicUrl: GeneratePublicAssetUrl(product.CoverImageUrl),
                    ShopId: product.ShopId,
                    ShopName: product.Shop.ShopName);
            })
            .ToList();
    }

    private async Task<List<CourseSearchResultDto>> MapCourseResultsAsync(
        List<ProductDocument> documents,
        CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
        {
            return new List<CourseSearchResultDto>();
        }

        var productIds = documents.Select(document => document.Id).ToList();
        var courses = await _dbContext.Courses
            .AsNoTracking()
            .Include(course => course.Product)
                .ThenInclude(product => product.Shop)
            .Where(course =>
                productIds.Contains(course.ProductId) &&
                course.Product.IsActive == true &&
                course.Product.Status == ProductStatus.Published &&
                course.Product.Shop.IsActive == true)
            .ToListAsync(cancellationToken);
        var courseMap = courses.ToDictionary(course => course.ProductId);

        return documents
            .Where(document => courseMap.ContainsKey(document.Id))
            .Select(document =>
            {
                var course = courseMap[document.Id];
                var product = course.Product;
                return new CourseSearchResultDto(
                    Id: course.Id,
                    Title: product.Title,
                    Description: product.Description,
                    Price: product.Price,
                    CoverImagePublicUrl: GeneratePublicAssetUrl(product.CoverImageUrl),
                    ShopId: product.ShopId,
                    ShopName: product.Shop.ShopName,
                    Level: course.Level,
                    TotalDurationInMinutes: course.TotalDurationInMinutes);
            })
            .ToList();
    }

    private async Task RecreateIndexAsync(
        string indexName,
        string logName,
        CancellationToken cancellationToken)
    {
        var existsResponse = await _client.Indices.ExistsAsync(indexName, cancellationToken);
        var existsStatusCode = existsResponse.ApiCallDetails?.HttpStatusCode;
        if (!existsResponse.IsValidResponse && existsStatusCode != 404)
        {
            _logger.LogError(
                "Elasticsearch {IndexName} index existence check failed. HTTP status: {HttpStatusCode}. Details: {DebugInformation}",
                logName,
                existsStatusCode,
                existsResponse.DebugInformation);
            throw new ExternalServiceException("Elasticsearch", $"{logName} arama indexi kontrol edilemedi.");
        }

        if (existsResponse.Exists)
        {
            var deleteResponse = await _client.Indices.DeleteAsync(indexName, cancellationToken);
            var deleteStatusCode = deleteResponse.ApiCallDetails?.HttpStatusCode;
            if (!deleteResponse.IsValidResponse && deleteStatusCode != 404)
            {
                _logger.LogError(
                    "Elasticsearch {IndexName} index cleanup failed. HTTP status: {HttpStatusCode}. Details: {DebugInformation}",
                    logName,
                    deleteStatusCode,
                    deleteResponse.DebugInformation);
                throw new ExternalServiceException("Elasticsearch", $"{logName} arama indexi temizlenemedi.");
            }
        }

        var createResponse = await _client.Indices.CreateAsync(indexName, cancellationToken);
        if (!createResponse.IsValidResponse)
        {
            _logger.LogError(
                "Elasticsearch {IndexName} index creation failed. HTTP status: {HttpStatusCode}. Details: {DebugInformation}",
                logName,
                createResponse.ApiCallDetails?.HttpStatusCode,
                createResponse.DebugInformation);
            throw new ExternalServiceException("Elasticsearch", $"{logName} arama indexi olusturulamadi.");
        }
    }

    private async Task RefreshIndexAsync(
        string indexName,
        string logName,
        CancellationToken cancellationToken)
    {
        var refreshResponse = await _client.Indices.RefreshAsync(indexName, cancellationToken);
        if (!refreshResponse.IsValidResponse)
        {
            _logger.LogError(
                "Elasticsearch {IndexName} index refresh failed. HTTP status: {HttpStatusCode}. Details: {DebugInformation}",
                logName,
                refreshResponse.ApiCallDetails?.HttpStatusCode,
                refreshResponse.DebugInformation);
            throw new ExternalServiceException("Elasticsearch", $"{logName} arama indexi yenilenemedi.");
        }
    }

    private static MediaDocument MapMediaDocument(Medium media)
    {
        return new MediaDocument
        {
            Id = media.Id,
            Caption = media.Caption,
            Hashtags = media.Hashtags ?? new List<string>(),
            ShopId = media.ShopId,
            ShopName = media.Shop.ShopName,
            ShopSlug = media.Shop.Slug,
            ProductId = media.ProductId,
            ProductTitle = media.Product?.Title,
            ProductType = ToProductTypeName(media.Product?.Type),
            ThumbnailObjectKey = ExtractObjectKey(media.ThumbnailUrl, PublicAssetsBucketName),
            VideoObjectKey = ExtractObjectKey(media.VideoUrl, PrivateProductsBucketName),
            ProductCoverImageObjectKey = ExtractObjectKey(media.Product?.CoverImageUrl, PublicAssetsBucketName),
            IsActive = media.IsActive == true,
            ShopIsActive = media.Shop.IsActive == true,
            CreatedAt = media.CreatedAt,
            ViewCount = media.ViewCount ?? 0,
            LikeCount = media.LikeCount ?? 0,
            SaveCount = media.SaveCount ?? 0,
            ShareCount = media.ShareCount ?? 0
        };
    }

    private static string? ToProductTypeName(ProductType? productType)
    {
        return productType switch
        {
            ProductType.Course => "course",
            ProductType.DigitalFile => "digital_file",
            _ => null
        };
    }

    private string? GeneratePublicAssetUrl(string? objectKey)
    {
        var normalizedObjectKey = ExtractObjectKey(objectKey, PublicAssetsBucketName);
        if (string.IsNullOrWhiteSpace(normalizedObjectKey))
        {
            return null;
        }

        return _storageService.GeneratePresignedDownloadUrl(
            PublicAssetsBucketName,
            normalizedObjectKey,
            PublicUrlExpiryMinutes);
    }

    private string? GeneratePrivateProductUrl(string? objectKey)
    {
        var normalizedObjectKey = ExtractObjectKey(objectKey, PrivateProductsBucketName);
        if (string.IsNullOrWhiteSpace(normalizedObjectKey))
        {
            return null;
        }

        return _storageService.GeneratePresignedDownloadUrl(
            PrivateProductsBucketName,
            normalizedObjectKey,
            PublicUrlExpiryMinutes);
    }

    private static string? ExtractObjectKey(string? urlOrObjectKey, string bucketName)
    {
        if (string.IsNullOrWhiteSpace(urlOrObjectKey))
        {
            return null;
        }

        if (!Uri.TryCreate(urlOrObjectKey, UriKind.Absolute, out var uri))
        {
            return urlOrObjectKey.TrimStart('/');
        }

        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        var bucketPrefix = $"{bucketName}/";
        var bucketIndex = path.IndexOf(bucketPrefix, StringComparison.OrdinalIgnoreCase);

        return bucketIndex >= 0
            ? path[(bucketIndex + bucketPrefix.Length)..]
            : path;
    }
}
