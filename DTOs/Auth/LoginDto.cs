using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Auth;

public sealed record LoginDto(
    [property: Required(ErrorMessage = "E-posta alanı zorunludur.")]
    [property: EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
    string Email,

    [property: Required(ErrorMessage = "Şifre alanı zorunludur.")]
    string Password);
