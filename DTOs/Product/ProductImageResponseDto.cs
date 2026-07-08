namespace CraftoraApi.DTOs.Product;

public sealed record ProductImageResponseDto(
    string ObjectKey,
    string? PublicUrl,
    int SortOrder);
