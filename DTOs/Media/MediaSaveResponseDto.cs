namespace CraftoraApi.DTOs.Media;

public sealed record MediaSaveResponseDto(
    bool IsSaved,
    int SaveCount);
