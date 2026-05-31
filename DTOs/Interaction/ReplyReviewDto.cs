using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Interaction;

public sealed record ReplyReviewDto(
    [property: Required(ErrorMessage = "Satici cevabi zorunludur.")]
    [property: MinLength(2, ErrorMessage = "Satici cevabi en az 2 karakter olmalidir.")]
    string SellerReply);
