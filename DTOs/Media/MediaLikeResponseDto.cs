namespace CraftoraApi.DTOs.Media;

public sealed record MediaLikeResponseDto(
    bool IsLiked,
    int LikeCount);
