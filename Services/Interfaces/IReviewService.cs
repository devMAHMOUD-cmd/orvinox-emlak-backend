using CraftoraApi.DTOs.Interaction;

namespace CraftoraApi.Services.Interfaces;

public interface IReviewService
{
    Task<ReviewResponseDto> AddReviewAsync(Guid userId, CreateReviewDto dto);

    Task<ReviewResponseDto> UpdateReviewAsync(Guid reviewId, Guid userId, UpdateReviewDto dto);

    Task DeleteReviewAsync(Guid reviewId, Guid userId);

    Task<ReviewResponseDto> ReplyToReviewAsync(Guid reviewId, Guid sellerUserId, ReplyReviewDto dto);

    Task<List<ReviewResponseDto>> GetProductReviewsAsync(Guid productId);
}
