using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Media;

public sealed record CreateMediaCommentDto(
    [property: Required(ErrorMessage = "Yorum metni zorunludur.")]
    [property: StringLength(1000, MinimumLength = 1, ErrorMessage = "Yorum metni 1 ile 1000 karakter arasinda olmalidir.")]
    string Text,

    Guid? ParentCommentId);
