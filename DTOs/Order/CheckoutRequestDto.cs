using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Order;

public sealed record CheckoutRequestDto(
    [property: Required(ErrorMessage = "Kart numarası zorunludur.")]
    string CardNumber,

    [property: Required(ErrorMessage = "Son kullanma tarihi zorunludur.")]
    string Expiry,

    [property: Required(ErrorMessage = "CVV zorunludur.")]
    string Cvv,

    List<CheckoutCouponDto>? Coupons = null);
