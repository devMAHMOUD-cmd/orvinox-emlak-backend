using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Coupon;

public sealed record ValidateCouponRequestDto(
    [property: Required(ErrorMessage = "ProductId zorunludur.")]
    Guid ProductId,

    [property: Required(ErrorMessage = "Kupon kodu zorunludur.")]
    string Code,

    [property: Range(typeof(decimal), "0", "999999999", ErrorMessage = "Sepet tutari negatif olamaz.")]
    decimal CartTotalAmount);
