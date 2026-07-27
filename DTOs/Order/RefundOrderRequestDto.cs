using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Order;

public sealed record RefundOrderRequestDto(
    [property: Required(ErrorMessage = "Iade nedeni zorunludur.")]
    [property: StringLength(500, MinimumLength = 3, ErrorMessage = "Iade nedeni 3 ile 500 karakter arasinda olmalidir.")]
    string Reason);
