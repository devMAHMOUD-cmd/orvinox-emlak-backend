using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Interaction;

public sealed record AnswerQuestionDto(
    [property: Required(ErrorMessage = "Cevap metni zorunludur.")]
    [property: MinLength(2, ErrorMessage = "Cevap metni en az 2 karakter olmalidir.")]
    string AnswerText);
