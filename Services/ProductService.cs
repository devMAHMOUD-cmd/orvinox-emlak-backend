using CraftoraApi.Data;
using CraftoraApi.DTOs.Product;
using CraftoraApi.Infrastructure.Messaging;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Elasticsearch;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Redis;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class ProductService : IProductService
{
    private const string PopularProductsCacheKey = "products:popular";

    private readonly AppDbContext _dbContext;
    private readonly IRabbitMqPublisher _rabbitMqPublisher;
    private readonly ICacheService _cacheService;

    public ProductService(
        AppDbContext dbContext,
        IRabbitMqPublisher rabbitMqPublisher,
        ICacheService cacheService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _rabbitMqPublisher = rabbitMqPublisher ?? throw new ArgumentNullException(nameof(rabbitMqPublisher));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
    }

    public async Task<ProductResponseDto> CreateProductAsync(Guid shopId, CreateProductDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var shopExists = await _dbContext.Shops.AnyAsync(shop => shop.Id == shopId && shop.IsActive == true);
        if (!shopExists)
        {
            throw new NotFoundException("Magaza bulunamadi.");
        }

        var categoryId = await ResolveCategoryIdAsync(dto.CategoryId);

        var product = new Product
        {
            ShopId = shopId,
            CategoryId = categoryId,
            Type = ProductType.DigitalFile,
            Title = dto.Title.Trim(),
            Description = dto.Description,
            Price = dto.Price,
            OriginalPrice = dto.OriginalPrice,
            Currency = "USD",
            CoverImageUrl = dto.CoverImageUrl,
            PreviewVideoUrl = dto.PreviewVideoUrl,
            FileUrl = dto.FileUrl,
            Metadata = dto.Metadata,
            Status = dto.Status,
            Tags = NormalizeTags(dto.Tags),
            RatingAverage = 0,
            ReviewCount = 0,
            SalesCount = 0,
            IsActive = true,
            IsFeatured = false
        };

        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();
        await InvalidatePopularProductsCacheAsync();
        await PublishProductIndexMessageAsync(product);

        return MapToResponse(product);
    }

    public async Task<ProductResponseDto> GetProductByIdAsync(Guid productId)
    {
        var product = await _dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(product =>
                product.Id == productId &&
                product.IsActive == true &&
                product.Status == ProductStatus.Published);

        if (product is null)
        {
            throw new NotFoundException("Urun bulunamadi.");
        }

        return MapToResponse(product);
    }

    public async Task<ProductListResponseDto> GetFilteredProductsAsync(
        Guid? categoryId,
        Guid? shopId,
        ProductStatus? status,
        bool includeAllStatuses,
        int pageNumber,
        int pageSize)
    {
        var normalizedPageNumber = Math.Max(pageNumber, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);

        if (IsPopularProductsRequest(
            categoryId,
            shopId,
            status,
            includeAllStatuses,
            normalizedPageNumber,
            normalizedPageSize))
        {
            var cachedPopularProducts = await _cacheService.GetAsync<ProductListResponseDto>(PopularProductsCacheKey);
            if (cachedPopularProducts is not null)
            {
                return cachedPopularProducts;
            }
        }

        var query = _dbContext.Products
            .AsNoTracking()
            .Where(product => product.IsActive == true);

        if (categoryId.HasValue)
        {
            query = query.Where(product => product.CategoryId == categoryId.Value);
        }

        if (shopId.HasValue)
        {
            query = query.Where(product => product.ShopId == shopId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(product => product.Status == status.Value);
        }
        else if (!includeAllStatuses)
        {
            query = query.Where(product => product.Status == ProductStatus.Published);
        }

        var totalCount = await query.CountAsync();
        var products = await query
            .OrderByDescending(product => product.IsFeatured == true)
            .ThenByDescending(product => product.SalesCount ?? 0)
            .ThenByDescending(product => product.CreatedAt)
            .Skip((normalizedPageNumber - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();

        var response = new ProductListResponseDto(
            TotalCount: totalCount,
            Items: products.Select(MapToResponse).ToList());

        if (IsPopularProductsRequest(
            categoryId,
            shopId,
            status,
            includeAllStatuses,
            normalizedPageNumber,
            normalizedPageSize))
        {
            await _cacheService.SetAsync(PopularProductsCacheKey, response, TimeSpan.FromHours(1));
        }

        return response;
    }

    public async Task<ProductResponseDto> UpdateProductAsync(Guid productId, Guid shopId, UpdateProductDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var product = await _dbContext.Products.FirstOrDefaultAsync(product =>
            product.Id == productId &&
            product.ShopId == shopId &&
            product.IsActive == true);

        if (product is null)
        {
            throw new NotFoundException("Urun bulunamadi.");
        }

        var categoryId = await ResolveCategoryIdAsync(dto.CategoryId);

        product.CategoryId = categoryId;
        product.Title = dto.Title.Trim();
        product.Description = dto.Description;
        product.Price = dto.Price;
        product.OriginalPrice = dto.OriginalPrice;
        product.CoverImageUrl = dto.CoverImageUrl;
        product.PreviewVideoUrl = dto.PreviewVideoUrl;
        product.FileUrl = dto.FileUrl;
        product.Metadata = dto.Metadata;
        product.Status = dto.Status;
        product.Tags = NormalizeTags(dto.Tags);

        await _dbContext.SaveChangesAsync();
        await InvalidatePopularProductsCacheAsync();
        await PublishProductIndexMessageAsync(product);

        return MapToResponse(product);
    }

    public async Task SoftDeleteProductAsync(Guid productId, Guid shopId)
    {
        var product = await _dbContext.Products.FirstOrDefaultAsync(product =>
            product.Id == productId &&
            product.ShopId == shopId &&
            product.IsActive == true);

        if (product is null)
        {
            throw new NotFoundException("Urun bulunamadi.");
        }

        product.IsActive = false;
        await _dbContext.SaveChangesAsync();
        await InvalidatePopularProductsCacheAsync();
        await _rabbitMqPublisher.PublishProductSyncMessage(new ProductSyncMessage(
            ProductId: product.Id,
            Action: "Delete",
            Document: null));
    }

    private async Task InvalidatePopularProductsCacheAsync()
    {
        await _cacheService.RemoveAsync(PopularProductsCacheKey);
    }

    private static bool IsPopularProductsRequest(
        Guid? categoryId,
        Guid? shopId,
        ProductStatus? status,
        bool includeAllStatuses,
        int pageNumber,
        int pageSize)
    {
        return !categoryId.HasValue &&
            !shopId.HasValue &&
            !includeAllStatuses &&
            (!status.HasValue || status.Value == ProductStatus.Published) &&
            pageNumber == 1 &&
            pageSize == 10;
    }

    private async Task PublishProductIndexMessageAsync(Product product)
    {
        var document = new ProductDocument
        {
            Id = product.Id,
            Name = product.Title,
            Description = product.Description,
            Price = product.Price,
            CategoryId = product.CategoryId,
            ShopId = product.ShopId,
            IsActive = product.IsActive == true && product.Status == ProductStatus.Published
        };

        await _rabbitMqPublisher.PublishProductSyncMessage(new ProductSyncMessage(
            ProductId: product.Id,
            Action: "Index",
            Document: document));
    }

    private async Task<Guid> ResolveCategoryIdAsync(string categoryIdOrSlug)
    {
        if (string.IsNullOrWhiteSpace(categoryIdOrSlug))
        {
            throw new BadRequestException("Kategori zorunludur.");
        }

        var normalizedCategory = categoryIdOrSlug.Trim();
        var category = Guid.TryParse(normalizedCategory, out var categoryId)
            ? await _dbContext.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == categoryId && item.IsActive)
            : await _dbContext.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Slug == normalizedCategory && item.IsActive);

        if (category is null)
        {
            throw new NotFoundException("Kategori bulunamadi.");
        }

        return category.Id;
    }

    private static ProductResponseDto MapToResponse(Product product)
    {
        return new ProductResponseDto(
            Id: product.Id,
            ShopId: product.ShopId,
            CategoryId: product.CategoryId,
            Title: product.Title,
            Description: product.Description ?? string.Empty,
            Price: product.Price,
            OriginalPrice: product.OriginalPrice,
            CoverImageUrl: product.CoverImageUrl,
            PreviewVideoUrl: product.PreviewVideoUrl,
            Status: product.Status,
            Tags: product.Tags ?? new List<string>(),
            RatingAverage: product.RatingAverage,
            ReviewCount: product.ReviewCount ?? 0,
            SalesCount: product.SalesCount ?? 0);
    }

    private static List<string> NormalizeTags(IEnumerable<string>? tags)
    {
        return tags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
    }
}
