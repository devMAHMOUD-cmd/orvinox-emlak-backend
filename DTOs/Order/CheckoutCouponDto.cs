using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Order;

public sealed record CheckoutCouponDto(
    [property: Required(ErrorMessage = "Kupon urunu zorunludur.")]
    Guid ProductId,

    [property: Required(ErrorMessage = "Kupon kodu zorunludur.")]
    [property: StringLength(50, MinimumLength = 2, ErrorMessage = "Kupon kodu 2 ile 50 karakter arasinda olmalidir.")]
    string Code);
