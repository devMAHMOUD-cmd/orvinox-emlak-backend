using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Auth;

public sealed record OtpDto(
    [property: Required(ErrorMessage = "E-posta alanı zorunludur.")]
    [property: EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
    string Email,

    [property: Required(ErrorMessage = "OTP kodu zorunludur.")]
    [property: RegularExpression(@"^\d{4}$", ErrorMessage = "OTP kodu tam olarak 4 haneli olmalıdır.")]
    string OtpCode);
