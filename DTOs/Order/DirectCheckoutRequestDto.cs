using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Order;

public sealed record DirectCheckoutRequestDto(
    [Required] Guid ProductId,
    [Required] string CardNumber,
    [Required] string Expiry,
    [Required] string Cvv);
