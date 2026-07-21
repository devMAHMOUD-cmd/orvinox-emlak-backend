using CraftoraApi.Data;
using CraftoraApi.DTOs.Interaction;
using CraftoraApi.Infrastructure.Security;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class ReviewService : IReviewService
{
    private const string PublicAssetsBucketName = "public-assets";
    private const int PublicAssetUrlExpiryMinutes = 60;
    private const int MaximumReviewsPerProduct = 3;

    private readonly AppDbContext _dbContext;
    private readonly IStorageService _storageService;

    public ReviewService(AppDbContext dbContext, IStorageService storageService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
    }

    public async Task<ReviewResponseDto> AddReviewAsync(Guid userId, CreateReviewDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var productExists = await _dbContext.Products.AnyAsync(product =>
            product.Id == dto.ProductId &&
            product.IsActive == true);
        if (!productExists)
        {
            throw new NotFoundException("Urun bulunamadi.");
        }

        var hasPurchasedProduct = await _dbContext.UserLibraries
            .AsNoTracking()
            .AnyAsync(libraryItem =>
                libraryItem.UserId == userId &&
                libraryItem.ProductId == dto.ProductId);
        if (!hasPurchasedProduct)
        {
            throw new ForbiddenException("Sadece satin aldiginiz urunlere yorum yapabilirsiniz.");
        }

        var comment = PlainTextInputValidator.Optional(dto.Comment, "Yorum metni", 2000);
        var images = NormalizeReviewImages(dto.Images);
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();

        try
        {
            var lockKey = $"review:{userId:N}:{dto.ProductId:N}";
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({lockKey}))");

            var reviewCount = await _dbContext.Reviews.CountAsync(review =>
                review.ProductId == dto.ProductId &&
                review.UserId == userId);
            if (reviewCount >= MaximumReviewsPerProduct)
            {
                throw new ConflictException("Bu urun icin en fazla 3 yorum yapabilirsiniz.");
            }

            var review = new Review
            {
                ProductId = dto.ProductId,
                UserId = userId,
                Rating = dto.Rating,
                Comment = comment,
                Images = images,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Reviews.Add(review);
            await _dbContext.SaveChangesAsync();
            await RefreshProductReviewStatsAsync(dto.ProductId);
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            return await GetReviewResponseAsync(review.Id);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<ReviewResponseDto> UpdateReviewAsync(Guid reviewId, Guid userId, UpdateReviewDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var review = await _dbContext.Reviews.FirstOrDefaultAsync(review =>
            review.Id == reviewId &&
            review.UserId == userId);
        if (review is null)
        {
            throw new NotFoundException("Yorum bulunamadi.");
        }

        review.Rating = dto.Rating;
        review.Comment = PlainTextInputValidator.Optional(dto.Comment, "Yorum metni", 2000);
        review.Images = NormalizeReviewImages(dto.Images);
        review.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        await RefreshProductReviewStatsAsync(review.ProductId);
        await _dbContext.SaveChangesAsync();

        return await GetReviewResponseAsync(review.Id);
    }

    public async Task DeleteReviewAsync(Guid reviewId, Guid userId)
    {
        var review = await _dbContext.Reviews.FirstOrDefaultAsync(review =>
            review.Id == reviewId &&
            review.UserId == userId);
        if (review is null)
        {
            throw new NotFoundException("Yorum bulunamadi.");
        }

        var productId = review.ProductId;
        _dbContext.Reviews.Remove(review);
        await _dbContext.SaveChangesAsync();

        await RefreshProductReviewStatsAsync(productId);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<ReviewResponseDto> ReplyToReviewAsync(Guid reviewId, Guid sellerUserId, ReplyReviewDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var review = await _dbContext.Reviews
            .Include(review => review.Product)
            .ThenInclude(product => product.Shop)
            .FirstOrDefaultAsync(review => review.Id == reviewId);

        if (review is null)
        {
            throw new NotFoundException("Yorum bulunamadi.");
        }

        if (review.Product.Shop.UserId != sellerUserId)
        {
            throw new ForbiddenException("Bu yoruma cevap verme yetkiniz yok.");
        }

        review.SellerReply = PlainTextInputValidator.Require(dto.SellerReply, "Satici cevabi", 2000);
        review.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return await GetReviewResponseAsync(review.Id);
    }

    public async Task<List<ReviewResponseDto>> GetProductReviewsAsync(Guid productId)
    {
        var productExists = await _dbContext.Products.AnyAsync(product =>
            product.Id == productId &&
            product.IsActive == true);
        if (!productExists)
        {
            throw new NotFoundException("Urun bulunamadi.");
        }

        var reviews = await _dbContext.Reviews
            .AsNoTracking()
            .Include(review => review.User)
            .Where(review => review.ProductId == productId)
            .OrderByDescending(review => review.CreatedAt)
            .ToListAsync();

        return reviews.Select(MapToResponse).ToList();
    }

    private async Task RefreshProductReviewStatsAsync(Guid productId)
    {
        var product = await _dbContext.Products.FirstOrDefaultAsync(product => product.Id == productId);
        if (product is null)
        {
            return;
        }

        var stats = await _dbContext.Reviews
            .Where(review => review.ProductId == productId)
            .GroupBy(review => review.ProductId)
            .Select(group => new
            {
                Count = group.Count(),
                Average = group.Average(review => review.Rating)
            })
            .FirstOrDefaultAsync();

        product.ReviewCount = stats?.Count ?? 0;
        product.RatingAverage = stats is null
            ? 0
            : Math.Round((decimal)stats.Average, 2);
    }

    private async Task<ReviewResponseDto> GetReviewResponseAsync(Guid reviewId)
    {
        var review = await _dbContext.Reviews
            .AsNoTracking()
            .Include(review => review.User)
            .FirstOrDefaultAsync(review => review.Id == reviewId);

        if (review is null)
        {
            throw new NotFoundException("Yorum bulunamadi.");
        }

        return MapToResponse(review);
    }

    private List<string> NormalizeReviewImages(IEnumerable<string>? images)
    {
        return images?
            .Select(ExtractPublicAssetObjectKey)
            .ToList() ?? new List<string>();
    }

    private string ExtractPublicAssetObjectKey(string imageReference)
    {
        if (string.IsNullOrWhiteSpace(imageReference))
        {
            throw new BadRequestException("Yorum gorsel anahtari bos olamaz.");
        }

        if (!Uri.TryCreate(imageReference, UriKind.Absolute, out var uri))
        {
            var objectKey = imageReference.TrimStart('/');
            if (!objectKey.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
            {
                throw new BadRequestException("Yorum gorselleri public-assets bucketindan yuklenmelidir.");
            }

            return objectKey;
        }

        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        var bucketPrefix = $"{PublicAssetsBucketName}/";
        var bucketIndex = path.IndexOf(bucketPrefix, StringComparison.OrdinalIgnoreCase);
        if (bucketIndex < 0)
        {
            throw new BadRequestException("Yorum gorselleri public-assets bucketindan yuklenmelidir.");
        }

        var objectKeyFromUrl = path[(bucketIndex + bucketPrefix.Length)..];
        if (!objectKeyFromUrl.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException("Yorum gorselleri public-assets bucketindan yuklenmelidir.");
        }

        return objectKeyFromUrl;
    }

    private ReviewResponseDto MapToResponse(Review review)
    {
        return new ReviewResponseDto(
            Id: review.Id,
            ProductId: review.ProductId,
            UserId: review.UserId,
            UserFullName: review.User?.FullName,
            Rating: review.Rating,
            Comment: review.Comment,
            Images: review.Images
                .Select(image => _storageService.GeneratePresignedDownloadUrl(
                    PublicAssetsBucketName,
                    image,
                    PublicAssetUrlExpiryMinutes))
                .ToList(),
            SellerReply: review.SellerReply,
            CreatedAt: review.CreatedAt,
            UpdatedAt: review.UpdatedAt);
    }
}
