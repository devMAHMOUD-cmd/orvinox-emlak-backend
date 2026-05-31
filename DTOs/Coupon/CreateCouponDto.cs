using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Coupon;

public sealed record CreateCouponDto(
    [property: Required(ErrorMessage = "ProductId zorunludur.")]
    Guid ProductId,

    [property: Required(ErrorMessage = "Kupon kodu zorunludur.")]
    [property: StringLength(50, MinimumLength = 2, ErrorMessage = "Kupon kodu 2 ile 50 karakter arasinda olmalidir.")]
    string Code,

    [property: Required(ErrorMessage = "Indirim tipi zorunludur.")]
    string DiscountType,

    [property: Range(typeof(decimal), "0.01", "999999999", ErrorMessage = "Indirim degeri 0'dan buyuk olmalidir.")]
    decimal DiscountValue,

    DateTime? ExpirationDate,

    [property: Range(1, int.MaxValue, ErrorMessage = "Kullanim limiti 1 veya daha buyuk olmalidir.")]
    int? UsageLimit,

    [property: Range(typeof(decimal), "0", "999999999", ErrorMessage = "Minimum sepet tutari negatif olamaz.")]
    decimal? MinimumCartAmount);
