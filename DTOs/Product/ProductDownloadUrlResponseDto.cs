namespace CraftoraApi.DTOs.Product;

public sealed record ProductDownloadUrlResponseDto(
    string DownloadUrl,
    DateTime ExpiresAt,
    string FileName);
