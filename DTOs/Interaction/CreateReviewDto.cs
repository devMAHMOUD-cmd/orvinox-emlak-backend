using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Interaction;

public sealed record CreateReviewDto(
    [property: Required(ErrorMessage = "ProductId zorunludur.")]
    Guid ProductId,

    [property: Range(1, 5, ErrorMessage = "Puan 1 ile 5 arasinda olmalidir.")]
    int Rating,

    string? Comment);
