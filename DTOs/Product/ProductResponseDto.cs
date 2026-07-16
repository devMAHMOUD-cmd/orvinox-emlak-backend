using CraftoraApi.Models.Enums;

namespace CraftoraApi.DTOs.Product;

public sealed record ProductResponseDto(
    Guid Id,
    Guid ShopId,
    Guid CategoryId,
    string Title,
    string Description,
    decimal Price,
    decimal? OriginalPrice,
    string? CoverImageUrl,
    string? CoverImagePublicUrl,
    string? PreviewVideoUrl,
    string? PreviewVideoPublicUrl,
    ProductStatus Status,
    List<string> Tags,
    decimal? RatingAverage,
    int ReviewCount,
    int SalesCount);
