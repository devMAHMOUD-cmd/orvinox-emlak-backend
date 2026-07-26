using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Cart;

public sealed record AddToCartDto(
    [property: Required(ErrorMessage = "ProductId zorunludur.")]
    Guid ProductId,

    [property: Range(1, 1, ErrorMessage = "Dijital urunler sepete yalnizca 1 adet eklenebilir.")]
    int Quantity);
