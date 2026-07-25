using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Subscription;

public sealed record StartSubscriptionRequestDto(
    [property: Required(ErrorMessage = "Kart numarası zorunludur.")]
    [property: RegularExpression(@"^\d{13,19}$", ErrorMessage = "Geçerli bir kart numarası giriniz (13-19 hane).")]
    string CardNumber,

    [property: Required(ErrorMessage = "Son kullanma tarihi zorunludur.")]
    [property: RegularExpression(@"^(0[1-9]|1[0-2])/\d{2}$", ErrorMessage = "Son kullanma tarihi MM/YY formatında giriniz.")]
    string Expiry,

    [property: Required(ErrorMessage = "CVV zorunludur.")]
    [property: RegularExpression(@"^\d{3,4}$", ErrorMessage = "CVV 3 veya 4 haneli olmalıdır.")]
    string Cvv,

    Guid? PlanId = null);
