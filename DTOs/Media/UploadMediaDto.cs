using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Media;

public sealed record UploadMediaDto(
    [property: Required(ErrorMessage = "ProductId zorunludur.")]
    Guid ProductId,

    [property: Required(ErrorMessage = "Açıklama zorunludur.")]
    [property: StringLength(2000, MinimumLength = 1, ErrorMessage = "Açıklama 1 ile 2000 karakter arasında olmalıdır.")]
    string Caption,

    [property: Required(ErrorMessage = "Orijinal dosya URL zorunludur.")]
    [property: StringLength(2048, ErrorMessage = "Video object key en fazla 2048 karakter olabilir.")]
    string OriginalFileUrl,

    [property: StringLength(2048, ErrorMessage = "Thumbnail object key en fazla 2048 karakter olabilir.")]
    string? ThumbnailUrl = null,

    List<string>? Hashtags = null);
