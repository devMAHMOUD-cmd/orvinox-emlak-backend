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
    private const string PopularProductsCacheKey = "products:popular:public-active-shops:v4";
    private const string PublicAssetsBucketName = "public-assets";
    private const string PrivateProductsBucketName = "private-products";
    private const int PublicAssetUrlExpiryMinutes = 60;
    private const int PrivateProductDownloadUrlExpiryMinutes = 60;

    private readonly AppDbContext _dbContext;
    private readonly IRabbitMqPublisher _rabbitMqPublisher;
    private readonly ICacheService _cacheService;
    private readonly IStorageService _storageService;
    private readonly IGamificationService _gamificationService;
    private readonly IUploadService _uploadService;
    private readonly ILogger<ProductService> _logger;

    public ProductService(
        AppDbContext dbContext,
        IRabbitMqPublisher rabbitMqPublisher,
        ICacheService cacheService,
        IStorageService storageService,
        IGamificationService gamificationService,
        IUploadService uploadService,
        ILogger<ProductService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _rabbitMqPublisher = rabbitMqPublisher ?? throw new ArgumentNullException(nameof(rabbitMqPublisher));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _gamificationService = gamificationService ?? throw new ArgumentNullException(nameof(gamificationService));
        _uploadService = uploadService ?? throw new ArgumentNullException(nameof(uploadService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProductResponseDto> CreateProductAsync(Guid shopId, CreateProductDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var shop = await _dbContext.Shops.FirstOrDefaultAsync(shop => shop.Id == shopId && shop.IsActive == true);
        if (shop is null)
        {
            throw new NotFoundException("Magaza bulunamadi.");
        }

        var categoryId = await ResolveCategoryIdAsync(dto.CategoryId);
        ValidateAssetOwnership(
            shop.UserId,
            dto.CoverImageUrl,
            dto.PreviewVideoUrl,
            dto.FileUrl,
            dto.ImageObjectKeys);
        await ValidateProductAssetsAsync(
            shop.UserId,
            dto.CoverImageUrl,
            dto.PreviewVideoUrl,
            dto.FileUrl,
            dto.ImageObjectKeys);

        var product = new Product
        {
            Id = Guid.NewGuid(),
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
        SyncProductImages(product, dto.ImageObjectKeys ?? BuildCoverImageList(dto.CoverImageUrl));
        await _dbContext.SaveChangesAsync();
        if (product.Status == ProductStatus.Published)
        {
            await TryAwardCreateProductPointsAsync(shop.UserId, product.Id);
        }
        await InvalidateProductCachesAsync(product.ShopId);
        await PublishProductIndexMessageAsync(product);

        return MapToResponse(product, includePrivateFileKey: true);
    }

    public async Task<ProductResponseDto> GetProductByIdAsync(Guid productId, Guid? currentUserId)
    {
        var product = await _dbContext.Products
            .AsNoTracking()
            .Include(item => item.ProductImages)
            .Include(item => item.Shop)
            .FirstOrDefaultAsync(product =>
                product.Id == productId &&
                product.IsActive == true &&
                ((product.Status == ProductStatus.Published && product.Shop.IsActive == true) ||
                 (currentUserId.HasValue && product.Shop.UserId == currentUserId.Value)));

        if (product is null)
        {
            throw new NotFoundException("Urun bulunamadi.");
        }

        var ownsProduct = currentUserId.HasValue && product.Shop.UserId == currentUserId.Value;
        return MapToResponse(product, includePrivateFileKey: ownsProduct);
    }

    public async Task<ProductDownloadUrlResponseDto> GenerateProductDownloadUrlAsync(Guid userId, Guid productId)
    {
        var product = await _dbContext.Products
            .AsNoTracking()
            .Include(item => item.Shop)
            .FirstOrDefaultAsync(item => item.Id == productId);

        if (product is null)
        {
            throw new NotFoundException("Urun bulunamadi.");
        }

        var hasPurchasedProduct = await _dbContext.UserLibraries
            .AsNoTracking()
            .AnyAsync(item => item.UserId == userId && item.ProductId == productId);

        if (!hasPurchasedProduct && product.Shop.UserId != userId)
        {
            throw new ForbiddenException("Bu urunu indirme yetkiniz yok.");
        }

        if (product.Type != ProductType.DigitalFile)
        {
            throw new BadRequestException("Bu urun indirilebilir bir dijital dosya degil.");
        }

        if (string.IsNullOrWhiteSpace(product.FileUrl))
        {
            throw new NotFoundException("Bu urun icin indirilebilir bir dosya bulunamadi.");
        }

        var productFileObjectKey = ExtractObjectKey(product.FileUrl, PrivateProductsBucketName);
        if (string.IsNullOrWhiteSpace(productFileObjectKey))
        {
            throw new NotFoundException("Bu urun icin indirilebilir bir dosya bulunamadi.");
        }

        var expiresAt = DateTime.UtcNow.AddMinutes(PrivateProductDownloadUrlExpiryMinutes);
        var downloadUrl = _storageService.GeneratePresignedDownloadUrl(
            PrivateProductsBucketName,
            productFileObjectKey,
            PrivateProductDownloadUrlExpiryMinutes);

        return new ProductDownloadUrlResponseDto(
            DownloadUrl: downloadUrl,
            ExpiresAt: expiresAt,
            FileName: GetFileName(productFileObjectKey));
    }

    public async Task<ProductListResponseDto> GetFilteredProductsAsync(
        Guid? categoryId,
        Guid? shopId,
        ProductStatus? status,
        bool includeAllStatuses,
        bool includeInactiveShopProducts,
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
            includeInactiveShopProducts,
            normalizedPageNumber,
            normalizedPageSize))
        {
            var cachedPopularProducts = await GetCachedPopularProductsIfStillPublicAsync();
            if (cachedPopularProducts is not null)
            {
                return cachedPopularProducts;
            }
        }

        var query = _dbContext.Products
            .AsNoTracking()
            .Include(item => item.ProductImages)
            .Include(item => item.Shop)
            .Where(product => product.IsActive == true);

        if (!includeInactiveShopProducts)
        {
            query = query.Where(product => product.Shop.IsActive == true);
        }

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
            Items: products
                .Select(product => MapToResponse(
                    product,
                    includePrivateFileKey: includeInactiveShopProducts))
                .ToList());

        if (IsPopularProductsRequest(
            categoryId,
            shopId,
            status,
            includeAllStatuses,
            includeInactiveShopProducts,
            normalizedPageNumber,
            normalizedPageSize))
        {
            await TrySetPopularProductsCacheAsync(response);
        }

        return response;
    }

    public async Task<ProductResponseDto> UpdateProductAsync(Guid productId, Guid shopId, UpdateProductDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var product = await _dbContext.Products
            .Include(item => item.ProductImages)
            .Include(item => item.Shop)
            .FirstOrDefaultAsync(product =>
                product.Id == productId &&
                product.ShopId == shopId &&
                product.IsActive == true);

        if (product is null)
        {
            throw new NotFoundException("Urun bulunamadi.");
        }

        var categoryId = await ResolveCategoryIdAsync(dto.CategoryId);
        ValidateAssetOwnership(
            product.Shop.UserId,
            dto.CoverImageUrl,
            dto.PreviewVideoUrl,
            dto.FileUrl,
            dto.ImageObjectKeys);
        await ValidateProductAssetsAsync(
            product.Shop.UserId,
            dto.CoverImageUrl,
            dto.PreviewVideoUrl,
            dto.FileUrl,
            dto.ImageObjectKeys);
        var previousStatus = product.Status;
        var previousCoverImageUrl = product.CoverImageUrl;
        var previousPreviewVideoUrl = product.PreviewVideoUrl;
        var previousFileUrl = product.FileUrl;
        var previousImageObjectKeys = product.ProductImages
            .Select(image => image.ObjectKey)
            .ToList();

        product.CategoryId = categoryId;
        product.Title = dto.Title.Trim();
        product.Description = dto.Description;
        product.Price = dto.Price;
        product.OriginalPrice = dto.OriginalPrice;
        product.CoverImageUrl = dto.CoverImageUrl;
        product.PreviewVideoUrl = dto.PreviewVideoUrl;
        if (dto.RemoveProductFile)
        {
            product.FileUrl = null;
        }
        else if (!string.IsNullOrWhiteSpace(dto.FileUrl))
        {
            product.FileUrl = dto.FileUrl;
        }
        product.Metadata = dto.Metadata;
        product.Status = dto.Status;
        product.Tags = NormalizeTags(dto.Tags);
        if (dto.ImageObjectKeys is not null)
        {
            SyncProductImages(product, dto.ImageObjectKeys);
        }

        await _dbContext.SaveChangesAsync();
        if (previousStatus != ProductStatus.Published && product.Status == ProductStatus.Published)
        {
            await TryAwardCreateProductPointsAsync(product.Shop.UserId, product.Id);
        }
        await InvalidateProductCachesAsync(product.ShopId);
        await PublishProductIndexMessageAsync(product);
        await DeleteReplacedProductAssetsAsync(
            previousCoverImageUrl,
            previousPreviewVideoUrl,
            previousFileUrl,
            previousImageObjectKeys,
            product);

        return MapToResponse(product, includePrivateFileKey: true);
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
        await InvalidateProductCachesAsync(product.ShopId);
        await TryPublishProductDeleteMessageAsync(product.Id);
    }

    private async Task InvalidatePopularProductsCacheAsync()
    {
        try
        {
            await _cacheService.RemoveAsync(PopularProductsCacheKey);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Popular products cache could not be invalidated.");
        }
    }

    private async Task InvalidateProductCachesAsync(Guid shopId)
    {
        await InvalidatePopularProductsCacheAsync();
        var shopSlug = await _dbContext.Shops
            .AsNoTracking()
            .Where(shop => shop.Id == shopId)
            .Select(shop => shop.Slug)
            .FirstOrDefaultAsync();
        if (!string.IsNullOrWhiteSpace(shopSlug))
        {
            try
            {
                await _cacheService.RemoveAsync(CacheKeys.PublicShopBySlug(shopSlug));
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Public shop cache could not be invalidated. ShopId: {ShopId}, Slug: {Slug}",
                    shopId,
                    shopSlug);
            }
        }
    }

    private async Task<ProductListResponseDto?> GetCachedPopularProductsIfStillPublicAsync()
    {
        ProductListResponseDto? cachedPopularProducts;
        try
        {
            cachedPopularProducts = await _cacheService.GetAsync<ProductListResponseDto>(PopularProductsCacheKey);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Popular products cache could not be read.");
            return null;
        }
        if (cachedPopularProducts is null || cachedPopularProducts.Items.Count == 0)
        {
            return cachedPopularProducts;
        }

        var cachedProductIds = cachedPopularProducts.Items
            .Select(item => item.Id)
            .Distinct()
            .ToList();

        var currentlyPublicProductCount = await _dbContext.Products
            .AsNoTracking()
            .CountAsync(product =>
                cachedProductIds.Contains(product.Id) &&
                product.IsActive == true &&
                product.Status == ProductStatus.Published &&
                product.Shop.IsActive == true);

        if (currentlyPublicProductCount == cachedProductIds.Count)
        {
            return cachedPopularProducts;
        }

        await InvalidatePopularProductsCacheAsync();
        return null;
    }

    private static bool IsPopularProductsRequest(
        Guid? categoryId,
        Guid? shopId,
        ProductStatus? status,
        bool includeAllStatuses,
        bool includeInactiveShopProducts,
        int pageNumber,
        int pageSize)
    {
        return !categoryId.HasValue &&
            !shopId.HasValue &&
            !includeAllStatuses &&
            !includeInactiveShopProducts &&
            (!status.HasValue || status.Value == ProductStatus.Published) &&
            pageNumber == 1 &&
            pageSize == 10;
    }

    private async Task PublishProductIndexMessageAsync(Product product)
    {
        try
        {
            var shopInfo = await _dbContext.Shops
                .AsNoTracking()
                .Where(shop => shop.Id == product.ShopId)
                .Select(shop => new
                {
                    IsActive = shop.IsActive == true,
                    shop.ShopName
                })
                .FirstOrDefaultAsync();

            var document = new ProductDocument
            {
                Id = product.Id,
                Name = product.Title,
                Description = product.Description,
                Type = product.Type == ProductType.Course ? "course" : "digital_file",
                Price = product.Price,
                CategoryId = product.CategoryId,
                ShopId = product.ShopId,
                ShopName = shopInfo?.ShopName,
                IsActive = product.IsActive == true,
                IsPublished = product.Status == ProductStatus.Published,
                ShopIsActive = shopInfo?.IsActive == true
            };

            await _rabbitMqPublisher.PublishProductSyncMessage(new ProductSyncMessage(
                ProductId: product.Id,
                Action: "Index",
                Document: document));
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Product search index message could not be published. ProductId: {ProductId}",
                product.Id);
        }
    }

    private async Task TryPublishProductDeleteMessageAsync(Guid productId)
    {
        try
        {
            await _rabbitMqPublisher.PublishProductSyncMessage(new ProductSyncMessage(
                ProductId: productId,
                Action: "Delete",
                Document: null));
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Product search delete message could not be published. ProductId: {ProductId}",
                productId);
        }
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

    private ProductResponseDto MapToResponse(Product product, bool includePrivateFileKey = false)
    {
        var productFileObjectKey = ExtractObjectKey(product.FileUrl, PrivateProductsBucketName);
        var hasProductFile = !string.IsNullOrWhiteSpace(productFileObjectKey);

        return new ProductResponseDto(
            Id: product.Id,
            ShopId: product.ShopId,
            CategoryId: product.CategoryId,
            Type: product.Type,
            Title: product.Title,
            Description: product.Description ?? string.Empty,
            Price: product.Price,
            OriginalPrice: product.OriginalPrice,
            CoverImageUrl: product.CoverImageUrl,
            CoverImagePublicUrl: GeneratePublicAssetUrl(product.CoverImageUrl),
            PreviewVideoUrl: product.PreviewVideoUrl,
            PreviewVideoPublicUrl: GeneratePublicAssetUrl(product.PreviewVideoUrl),
            FileUrl: includePrivateFileKey ? productFileObjectKey : null,
            HasProductFile: hasProductFile,
            ProductFileName: hasProductFile ? GetFileName(productFileObjectKey!) : null,
            Images: product.ProductImages
                .OrderBy(image => image.SortOrder)
                .Select(image => new ProductImageResponseDto(
                    image.ObjectKey,
                    GeneratePublicAssetUrl(image.ObjectKey),
                    image.SortOrder))
                .ToList(),
            Status: product.Status,
            Tags: product.Tags ?? new List<string>(),
            Metadata: product.Metadata,
            RatingAverage: product.RatingAverage,
            ReviewCount: product.ReviewCount ?? 0,
            SalesCount: product.SalesCount ?? 0);
    }

    private async Task TrySetPopularProductsCacheAsync(ProductListResponseDto response)
    {
        try
        {
            await _cacheService.SetAsync(PopularProductsCacheKey, response, TimeSpan.FromHours(1));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Popular products cache could not be written.");
        }
    }

    private async Task TryAwardCreateProductPointsAsync(Guid userId, Guid productId)
    {
        try
        {
            await _gamificationService.AwardPointsAsync(
                userId,
                "create_product",
                5m,
                productId,
                preventDuplicate: true);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Create product points could not be awarded. UserId: {UserId}, ProductId: {ProductId}",
                userId,
                productId);
        }
    }

    private static void ValidateAssetOwnership(
        Guid ownerUserId,
        string? coverImageUrl,
        string? previewVideoUrl,
        string? fileUrl,
        IEnumerable<string>? imageObjectKeys)
    {
        var expectedPrefix = $"users/{ownerUserId:D}/";
        var assetKeys = (imageObjectKeys ?? [])
            .Append(coverImageUrl)
            .Append(previewVideoUrl)
            .Append(fileUrl)
            .Where(value => !string.IsNullOrWhiteSpace(value));

        foreach (var value in assetKeys)
        {
            var normalizedValue = Uri.TryCreate(value, UriKind.Absolute, out var uri)
                ? Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/')
                : value!.Trim().TrimStart('/');
            var usersSegmentIndex = normalizedValue.IndexOf("users/", StringComparison.OrdinalIgnoreCase);
            if (usersSegmentIndex < 0)
            {
                continue;
            }

            var userScopedKey = normalizedValue[usersSegmentIndex..];
            if (!userScopedKey.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ForbiddenException("Baska bir kullaniciya ait dosya bu urune baglanamaz.");
            }
        }
    }

    private async Task ValidateProductAssetsAsync(
        Guid userId,
        string? coverImageUrl,
        string? previewVideoUrl,
        string? fileUrl,
        IEnumerable<string>? imageObjectKeys)
    {
        var publicKeys = (imageObjectKeys ?? [])
            .Append(coverImageUrl)
            .Append(previewVideoUrl)
            .Select(value => GetUserScopedObjectKey(userId, value, PublicAssetsBucketName))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal);
        foreach (var objectKey in publicKeys)
        {
            await _uploadService.ValidateOwnedObjectAsync(userId, objectKey!, isPublic: true);
        }

        var privateObjectKey = GetUserScopedObjectKey(userId, fileUrl, PrivateProductsBucketName);
        if (!string.IsNullOrWhiteSpace(privateObjectKey))
        {
            await _uploadService.ValidateOwnedObjectAsync(userId, privateObjectKey, isPublic: false);
        }
    }

    private static string? GetUserScopedObjectKey(
        Guid userId,
        string? urlOrObjectKey,
        string bucketName)
    {
        var objectKey = ExtractObjectKey(urlOrObjectKey, bucketName);
        return objectKey?.StartsWith(
            $"users/{userId:D}/",
            StringComparison.OrdinalIgnoreCase) == true
            ? objectKey
            : null;
    }

    private void SyncProductImages(Product product, IEnumerable<string>? objectKeys)
    {
        var normalizedKeys = objectKeys?
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => ExtractObjectKey(key, PublicAssetsBucketName))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!)
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToList() ?? [];

        var removedImages = product.ProductImages
            .Where(image => !normalizedKeys.Contains(image.ObjectKey, StringComparer.Ordinal))
            .ToList();
        if (removedImages.Count > 0)
        {
            _dbContext.ProductImages.RemoveRange(removedImages);
            foreach (var removedImage in removedImages)
            {
                product.ProductImages.Remove(removedImage);
            }
        }

        for (var index = 0; index < normalizedKeys.Count; index++)
        {
            var objectKey = normalizedKeys[index];
            var existingImage = product.ProductImages.FirstOrDefault(
                image => image.ObjectKey == objectKey);
            if (existingImage is not null)
            {
                existingImage.SortOrder = index;
                continue;
            }

            product.ProductImages.Add(new ProductImage
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                ObjectKey = objectKey,
                SortOrder = index,
                CreatedAt = DateTime.UtcNow
            });
        }
    }

    private static IReadOnlyList<string> BuildCoverImageList(string? coverImageUrl)
    {
        return string.IsNullOrWhiteSpace(coverImageUrl)
            ? []
            : [coverImageUrl];
    }

    private async Task DeleteReplacedProductAssetsAsync(
        string? previousCoverImageUrl,
        string? previousPreviewVideoUrl,
        string? previousFileUrl,
        IReadOnlyCollection<string> previousImageObjectKeys,
        Product product)
    {
        var currentPublicKeys = product.ProductImages
            .Select(image => image.ObjectKey)
            .Append(product.CoverImageUrl)
            .Append(product.PreviewVideoUrl)
            .Select(value => ExtractObjectKey(value, PublicAssetsBucketName))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToHashSet(StringComparer.Ordinal);

        var previousPublicKeys = previousImageObjectKeys
            .Append(previousCoverImageUrl)
            .Append(previousPreviewVideoUrl)
            .Select(value => ExtractObjectKey(value, PublicAssetsBucketName))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal);

        foreach (var objectKey in previousPublicKeys)
        {
            if (!currentPublicKeys.Contains(objectKey))
            {
                await TryDeleteGeneratedObjectAsync(PublicAssetsBucketName, objectKey);
            }
        }

        var previousPrivateKey = ExtractObjectKey(previousFileUrl, PrivateProductsBucketName);
        var currentPrivateKey = ExtractObjectKey(product.FileUrl, PrivateProductsBucketName);
        if (!string.Equals(previousPrivateKey, currentPrivateKey, StringComparison.Ordinal))
        {
            await TryDeleteGeneratedObjectAsync(PrivateProductsBucketName, previousPrivateKey);
        }
    }

    private async Task TryDeleteGeneratedObjectAsync(string bucketName, string? objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey) ||
            !objectKey.StartsWith("users/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await _storageService.DeleteFileAsync(bucketName, objectKey);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Replaced product asset could not be deleted. BucketName: {BucketName}, ObjectKey: {ObjectKey}",
                bucketName,
                objectKey);
        }
    }

    private string? GeneratePublicAssetUrl(string? urlOrObjectKey)
    {
        var objectKey = ExtractObjectKey(urlOrObjectKey, PublicAssetsBucketName);
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return null;
        }

        return _storageService.GeneratePresignedDownloadUrl(
            PublicAssetsBucketName,
            objectKey,
            PublicAssetUrlExpiryMinutes);
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

    private static string GetFileName(string objectKey)
    {
        var normalizedObjectKey = objectKey.Trim();
        var separatorIndex = normalizedObjectKey.LastIndexOf('/');

        return separatorIndex >= 0 && separatorIndex < normalizedObjectKey.Length - 1
            ? normalizedObjectKey[(separatorIndex + 1)..]
            : normalizedObjectKey;
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
