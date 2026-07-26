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
    string? FileUrl,
    bool HasProductFile,
    string? ProductFileName,
    IReadOnlyList<ProductImageResponseDto> Images,
    ProductStatus Status,
    List<string> Tags,
    string? Metadata,
    decimal? RatingAverage,
    int ReviewCount,
    int SalesCount);
