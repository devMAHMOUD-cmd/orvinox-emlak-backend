using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Media;

public sealed record UpdateMediaDto(
    [property: Required(ErrorMessage = "Açıklama zorunludur.")]
    string Caption,

    Guid? ProductId,

    string? ThumbnailUrl,

    string? Status,

    List<string>? Hashtags);
