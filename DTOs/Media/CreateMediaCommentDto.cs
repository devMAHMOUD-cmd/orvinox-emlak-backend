using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Media;

public sealed record CreateMediaCommentDto(
    [property: Required(ErrorMessage = "Yorum metni zorunludur.")]
    string Text,

    Guid? ParentCommentId);
