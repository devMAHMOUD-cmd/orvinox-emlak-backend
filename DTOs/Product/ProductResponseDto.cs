using CraftoraApi.Models.Enums;

namespace CraftoraApi.DTOs.Product;

public sealed record ProductResponseDto(
    Guid Id,
    Guid ShopId,
    Guid CategoryId,
    ProductType Type,
    string Title,
    string Description,
    decimal Price,
    decimal? OriginalPrice,
    string? CoverImageUrl,
    string? CoverImagePublicUrl,
    string? PreviewVideoUrl,
    string? PreviewVideoPublicUrl,
    bool HasProductFile,
    string? ProductFileName,
    ProductStatus Status,
    List<string> Tags,
    decimal? RatingAverage,
    int ReviewCount,
    int SalesCount);
