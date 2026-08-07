using System.ComponentModel.DataAnnotations;
using CraftoraApi.Models.Enums;

namespace CraftoraApi.DTOs.Product;

public sealed record UpdateProductDto(
    [property: Required(ErrorMessage = "Kategori zorunludur.")]
    [property: StringLength(255, ErrorMessage = "Kategori kimligi en fazla 255 karakter olabilir.")]
    string CategoryId,

    [property: Required(ErrorMessage = "Ürün başlığı zorunludur.")]
    [property: StringLength(255, MinimumLength = 3, ErrorMessage = "Ürün başlığı 3 ile 255 karakter arasında olmalıdır.")]
    string Title,

    [property: Required(ErrorMessage = "Ürün açıklaması zorunludur.")]
    [property: StringLength(20000, ErrorMessage = "Ürün açıklaması en fazla 20000 karakter olabilir.")]
    string Description,

    [property: Range(0d, 99999999.99d, ErrorMessage = "Fiyat 0 ile 99999999.99 arasinda olmalidir.")]
    decimal Price,

    [property: Range(0d, 99999999.99d, ErrorMessage = "Orijinal fiyat 0 ile 99999999.99 arasinda olmalidir.")]
    decimal? OriginalPrice,

    ProductStatus Status,

    List<string> Tags,

    [property: StringLength(1024, ErrorMessage = "Kapak görseli anahtarı en fazla 1024 karakter olabilir.")]
    string? CoverImageUrl,

    [property: StringLength(1024, ErrorMessage = "Önizleme videosu anahtarı en fazla 1024 karakter olabilir.")]
    string? PreviewVideoUrl,

    [property: StringLength(1024, ErrorMessage = "Ürün dosyası anahtarı en fazla 1024 karakter olabilir.")]
    string? FileUrl,

    [property: StringLength(20000, ErrorMessage = "Metadata en fazla 20000 karakter olabilir.")]
    string? Metadata,

    ProductType? Type = null,

    IReadOnlyList<string>? ImageObjectKeys = null,

    bool RemoveProductFile = false,

    [property: StringLength(3, ErrorMessage = "Para birimi en fazla 3 karakter olabilir.")]
    string? Currency = null);
