using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Cart;

public sealed record UpdateCartItemDto(
    [property: Range(1, int.MaxValue, ErrorMessage = "Miktar 1 veya daha buyuk olmalidir.")]
    int Quantity);
