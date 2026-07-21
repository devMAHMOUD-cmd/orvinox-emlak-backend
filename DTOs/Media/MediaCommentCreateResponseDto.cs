namespace CraftoraApi.DTOs.Media;

public sealed record MediaCommentCreateResponseDto(
    CommentDto Comment,
    int CommentCount);
