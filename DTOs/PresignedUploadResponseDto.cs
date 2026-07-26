namespace CraftoraApi.DTOs;

public sealed record PresignedUploadResponseDto(
    string UploadUrl,
    string ObjectKey);
