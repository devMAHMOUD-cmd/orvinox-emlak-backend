using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Media;

public sealed record UploadMediaDto(
    [property: Required(ErrorMessage = "ProductId zorunludur.")]
    Guid ProductId,

    [property: Required(ErrorMessage = "Açıklama zorunludur.")]
    string Caption,

    [property: Required(ErrorMessage = "Orijinal dosya URL zorunludur.")]
    string OriginalFileUrl,

    string? ThumbnailUrl = null,

    List<string>? Hashtags = null);
