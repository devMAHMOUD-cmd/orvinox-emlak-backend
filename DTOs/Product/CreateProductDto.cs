using System.ComponentModel.DataAnnotations;
using CraftoraApi.Models.Enums;

namespace CraftoraApi.DTOs.Product;

public sealed record CreateProductDto(
    [property: Required(ErrorMessage = "Kategori zorunludur.")]
    string CategoryId,

    [property: Required(ErrorMessage = "Ürün başlığı zorunludur.")]
    [property: StringLength(255, MinimumLength = 3, ErrorMessage = "Ürün başlığı 3 ile 255 karakter arasında olmalıdır.")]
    string Title,

    [property: Required(ErrorMessage = "Ürün açıklaması zorunludur.")]
    string Description,

    [property: Range(0, double.MaxValue, ErrorMessage = "Fiyat negatif olamaz.")]
    decimal Price,

    [property: Range(0, double.MaxValue, ErrorMessage = "Orijinal fiyat negatif olamaz.")]
    decimal? OriginalPrice,

    ProductStatus Status,

    List<string> Tags,

    string? CoverImageUrl,

    string? PreviewVideoUrl,

    string? FileUrl,

    string? Metadata);
