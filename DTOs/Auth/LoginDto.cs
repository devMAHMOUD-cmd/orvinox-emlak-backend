using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Auth;

public sealed record LoginDto(
    [property: Required(ErrorMessage = "E-posta alanı zorunludur.")]
    [property: EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
    [property: StringLength(255, ErrorMessage = "E-posta en fazla 255 karakter olabilir.")]
    string Email,

    [property: Required(ErrorMessage = "Şifre alanı zorunludur.")]
    [property: StringLength(128, MinimumLength = 1, ErrorMessage = "Şifre en fazla 128 karakter olabilir.")]
    string Password);
