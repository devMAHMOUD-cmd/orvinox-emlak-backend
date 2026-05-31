using CraftoraApi.Data;
using CraftoraApi.DTOs.Interaction;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class ReviewService : IReviewService
{
    private readonly AppDbContext _dbContext;

    public ReviewService(AppDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
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

        var alreadyReviewed = await _dbContext.Reviews.AnyAsync(review =>
            review.ProductId == dto.ProductId &&
            review.UserId == userId);
        if (alreadyReviewed)
        {
            throw new ConflictException("Bu urune zaten yorum yaptiniz.");
        }

        var review = new Review
        {
            ProductId = dto.ProductId,
            UserId = userId,
            Rating = dto.Rating,
            Comment = dto.Comment,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Reviews.Add(review);
        await _dbContext.SaveChangesAsync();

        await RefreshProductReviewStatsAsync(dto.ProductId);
        await _dbContext.SaveChangesAsync();

        return await GetReviewResponseAsync(review.Id);
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
        review.Comment = dto.Comment;
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

        review.SellerReply = dto.SellerReply.Trim();
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

    private static ReviewResponseDto MapToResponse(Review review)
    {
        return new ReviewResponseDto(
            Id: review.Id,
            ProductId: review.ProductId,
            UserId: review.UserId,
            UserFullName: review.User?.FullName,
            Rating: review.Rating,
            Comment: review.Comment,
            SellerReply: review.SellerReply,
            CreatedAt: review.CreatedAt,
            UpdatedAt: review.UpdatedAt);
    }
}
