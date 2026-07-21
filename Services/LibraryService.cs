using CraftoraApi.Data;
using CraftoraApi.DTOs.Library;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class LibraryService : ILibraryService
{
    private const string PublicAssetsBucketName = "public-assets";
    private const int PublicAssetUrlExpiryMinutes = 60;

    private readonly AppDbContext _dbContext;
    private readonly IStorageService _storageService;

    public LibraryService(AppDbContext dbContext, IStorageService storageService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
    }

    public async Task<List<LibraryItemDto>> GetMyLibraryAsync(Guid userId)
    {
        var libraryItems = await _dbContext.UserLibraries
            .AsNoTracking()
            .Include(item => item.Product)
                .ThenInclude(product => product.Shop)
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.LastAccessedAt ?? item.PurchasedAt)
            .ToListAsync();

        return libraryItems.Select(MapToDto).ToList();
    }

    public async Task MarkAsAccessedAsync(Guid userId, Guid productId)
    {
        var libraryItem = await _dbContext.UserLibraries.FirstOrDefaultAsync(item =>
            item.UserId == userId &&
            item.ProductId == productId);

        if (libraryItem is null)
        {
            throw new NotFoundException("Kutuphane urunu bulunamadi.");
        }

        libraryItem.LastAccessedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }

    private LibraryItemDto MapToDto(UserLibrary item)
    {
        var product = item.Product;
        var hasProductFile = !string.IsNullOrWhiteSpace(product.FileUrl);

        return new LibraryItemDto(
            Id: item.Id,
            ProductId: item.ProductId,
            ProductTitle: product.Title,
            ProductType: ToProductTypeName(product.Type),
            CoverImageUrl: product.CoverImageUrl,
            CoverImagePublicUrl: GeneratePublicAssetUrl(product.CoverImageUrl),
            ShopName: product.Shop.ShopName,
            HasProductFile: hasProductFile,
            ProductFileName: hasProductFile ? GetFileName(product.FileUrl!) : null,
            ProductIsActive: product.IsActive == true,
            IsArchived: product.IsActive != true || product.Status == ProductStatus.Archived,
            PurchasedAt: item.PurchasedAt,
            LastAccessedAt: item.LastAccessedAt);
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

    private static string ToProductTypeName(ProductType productType)
    {
        return productType switch
        {
            ProductType.DigitalFile => "digital_file",
            ProductType.Course => "course",
            _ => productType.ToString().ToLowerInvariant()
        };
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
}
