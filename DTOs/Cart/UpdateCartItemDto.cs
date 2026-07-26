using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Cart;

public sealed record UpdateCartItemDto(
    [property: Range(1, 1, ErrorMessage = "Dijital urunlerde miktar 1 olmalidir.")]
    int Quantity);
