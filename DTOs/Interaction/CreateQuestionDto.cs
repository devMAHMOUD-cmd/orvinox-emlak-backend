using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Interaction;

public sealed record CreateQuestionDto(
    [property: Required(ErrorMessage = "ProductId zorunludur.")]
    Guid ProductId,

    [property: Required(ErrorMessage = "Soru metni zorunludur.")]
    [property: StringLength(500, MinimumLength = 2, ErrorMessage = "Soru metni 2 ile 500 karakter arasinda olmalidir.")]
    string QuestionText);
