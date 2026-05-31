using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Cart;

public sealed record AddToCartDto(
    [property: Required(ErrorMessage = "ProductId zorunludur.")]
    Guid ProductId,

    [property: Range(1, int.MaxValue, ErrorMessage = "Miktar 1 veya daha buyuk olmalidir.")]
    int Quantity);
