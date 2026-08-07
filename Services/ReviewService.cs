using CraftoraApi.Data;
using CraftoraApi.DTOs.Interaction;
using CraftoraApi.DTOs.Notification;
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
    private readonly IUploadService _uploadService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<ReviewService> _logger;

    public ReviewService(
        AppDbContext dbContext,
        IStorageService storageService,
        IUploadService uploadService,
        INotificationService notificationService,
        ILogger<ReviewService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _uploadService = uploadService ?? throw new ArgumentNullException(nameof(uploadService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ReviewResponseDto> AddReviewAsync(Guid userId, CreateReviewDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var product = await _dbContext.Products
            .AsNoTracking()
            .Where(product => product.Id == dto.ProductId && product.IsActive == true)
            .Select(product => new
            {
                product.Id,
                product.Title,
                product.ShopId,
                OwnerUserId = product.Shop.UserId
            })
            .FirstOrDefaultAsync();
        if (product is null)
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
        var images = await NormalizeReviewImagesAsync(userId, dto.Images);
        var actor = await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new
            {
                user.FullName,
                user.AvatarUrl,
                ShopId = (Guid?)user.Shop!.Id,
                ShopName = user.Shop != null ? user.Shop.ShopName : null,
                ShopLogoUrl = user.Shop != null ? user.Shop.LogoUrl : null
            })
            .FirstAsync();
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

            if (product.OwnerUserId != userId)
            {
                try
                {
                    var reviewSummary = string.IsNullOrWhiteSpace(comment)
                        ? $"{dto.Rating}/5 puan verdi."
                        : $"{dto.Rating}/5 puan verdi: {Preview(comment, 600)}";

                    await _notificationService.SendActorNotificationAsync(
                        product.OwnerUserId,
                        "Urunune yeni bir yorum geldi",
                        $"{DisplayName(actor.FullName)}, {product.Title} urunune {reviewSummary}",
                        NotificationType.NewReview,
                        product.Id,
                        userId,
                        actor.FullName,
                        actor.AvatarUrl,
                        actor.ShopId,
                        actor.ShopName,
                        actor.ShopLogoUrl,
                        "product");
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Product review notification failed after review was saved. ReviewId: {ReviewId}",
                        review.Id);
                }
            }

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

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        review.Rating = dto.Rating;
        review.Comment = PlainTextInputValidator.Optional(dto.Comment, "Yorum metni", 2000);
        review.Images = await NormalizeReviewImagesAsync(userId, dto.Images);
        review.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        await RefreshProductReviewStatsAsync(review.ProductId);
        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

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

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        var productId = review.ProductId;
        _dbContext.Reviews.Remove(review);
        await _dbContext.SaveChangesAsync();

        await RefreshProductReviewStatsAsync(productId);
        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
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

        var sellerReply = PlainTextInputValidator.Require(dto.SellerReply, "Satici cevabi", 2000);
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             SELECT public.set_review_seller_reply(
                 {review.Id},
                 {sellerUserId},
                 {sellerReply})
             """);

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
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT public.refresh_product_review_stats({productId})");
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

    private async Task<List<string>> NormalizeReviewImagesAsync(
        Guid userId,
        IEnumerable<string>? images)
    {
        var normalizedImages = images?
            .Select(image => ExtractPublicAssetObjectKey(userId, image))
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? new List<string>();

        foreach (var objectKey in normalizedImages)
        {
            await _uploadService.ValidatePublicImageAsync(userId, objectKey);
        }

        return normalizedImages;
    }

    private static string ExtractPublicAssetObjectKey(Guid userId, string imageReference)
    {
        if (string.IsNullOrWhiteSpace(imageReference))
        {
            throw new BadRequestException("Yorum gorsel anahtari bos olamaz.");
        }

        if (!Uri.TryCreate(imageReference, UriKind.Absolute, out var uri))
        {
            var objectKey = imageReference.TrimStart('/');
            if (!IsOwnedPublicObjectKey(userId, objectKey))
            {
                throw new ForbiddenException("Baska bir kullaniciya ait gorsel yoruma eklenemez.");
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
        if (!IsOwnedPublicObjectKey(userId, objectKeyFromUrl))
        {
            throw new ForbiddenException("Baska bir kullaniciya ait gorsel yoruma eklenemez.");
        }

        return objectKeyFromUrl;
    }

    private static bool IsOwnedPublicObjectKey(Guid userId, string objectKey)
    {
        return objectKey.StartsWith(
            $"users/{userId:D}/public/",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string DisplayName(string? fullName) =>
        string.IsNullOrWhiteSpace(fullName) ? "Bir kullanici" : fullName.Trim();

    private static string Preview(string value, int maxLength) =>
        value.Length <= maxLength ? value : $"{value[..maxLength].TrimEnd()}...";

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
