using System.ComponentModel.DataAnnotations;
using CraftoraApi.Models.Enums;

namespace CraftoraApi.DTOs.Product;

public sealed record UpdateProductDto(
    [property: Required(ErrorMessage = "Kategori zorunludur.")]
    string CategoryId,

    [property: Required(ErrorMessage = "Ürün başlığı zorunludur.")]
    [property: StringLength(255, MinimumLength = 3, ErrorMessage = "Ürün başlığı 3 ile 255 karakter arasında olmalıdır.")]
    string Title,

    [property: Required(ErrorMessage = "Ürün açıklaması zorunludur.")]
    string Description,

    [property: Range(typeof(decimal), "0", "99999999.99", ErrorMessage = "Fiyat 0 ile 99999999.99 arasinda olmalidir.")]
    decimal Price,

    [property: Range(typeof(decimal), "0", "99999999.99", ErrorMessage = "Orijinal fiyat 0 ile 99999999.99 arasinda olmalidir.")]
    decimal? OriginalPrice,

    ProductStatus Status,

    List<string> Tags,

    string? CoverImageUrl,

    string? PreviewVideoUrl,

    string? FileUrl,

    string? Metadata,

    ProductType? Type = null);
