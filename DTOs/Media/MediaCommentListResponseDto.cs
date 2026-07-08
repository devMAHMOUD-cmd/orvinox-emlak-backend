namespace CraftoraApi.DTOs.Media;

public sealed record MediaCommentListResponseDto(
    IReadOnlyList<CommentDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
