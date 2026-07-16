using CraftoraApi.DTOs.Media;

namespace CraftoraApi.Services.Interfaces;

public interface IMediaService
{
    Task<List<MediaResponseDto>> GetFeedAsync(Guid? currentUserId, int page = 1, int pageSize = 10);

    Task<List<MediaResponseDto>> GetShopMediaAsync(Guid shopId, int page = 1, int pageSize = 10);

    Task<List<MediaResponseDto>> GetMyMediaAsync(Guid userId, int page = 1, int pageSize = 12);

    Task<MediaResponseDto> UploadMediaAsync(Guid userId, UploadMediaDto dto);

    Task ToggleLikeAsync(Guid mediaId, Guid userId);

    Task ToggleSaveAsync(Guid mediaId, Guid userId);

    Task<MediaResponseDto> RecordShareAsync(Guid mediaId, Guid userId);

    Task<CommentDto> AddCommentAsync(Guid mediaId, Guid userId, string text, Guid? parentCommentId);

    Task<MediaCommentListResponseDto> GetCommentsAsync(Guid mediaId, int page = 1, int pageSize = 20);

    Task DeleteCommentAsync(Guid commentId, Guid userId);

    Task DeleteMediaAsync(Guid mediaId, Guid userId);

    Task RecordViewAsync(Guid mediaId, Guid? userId);
}
