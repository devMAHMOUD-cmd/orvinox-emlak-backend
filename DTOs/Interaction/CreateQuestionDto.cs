using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Interaction;

public sealed record CreateQuestionDto(
    [property: Required(ErrorMessage = "ProductId zorunludur.")]
    Guid ProductId,

    [property: Required(ErrorMessage = "Soru metni zorunludur.")]
    [property: MinLength(2, ErrorMessage = "Soru metni en az 2 karakter olmalidir.")]
    string QuestionText);
