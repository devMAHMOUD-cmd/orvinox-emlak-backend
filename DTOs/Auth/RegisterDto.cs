using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Auth;

public sealed record RegisterDto(
    [property: Required(ErrorMessage = "Ad soyad alanı zorunludur.")]
    [property: MinLength(3, ErrorMessage = "Ad soyad en az 3 karakter olmalıdır.")]
    [property: MaxLength(100, ErrorMessage = "Ad soyad en fazla 100 karakter olabilir.")]
    string FullName,

    [property: Required(ErrorMessage = "E-posta alanı zorunludur.")]
    [property: EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
    [property: StringLength(255, ErrorMessage = "E-posta en fazla 255 karakter olabilir.")]
    string Email,

    [property: Required(ErrorMessage = "Şifre alanı zorunludur.")]
    [property: StringLength(128, MinimumLength = 8, ErrorMessage = "Şifre 8 ile 128 karakter arasında olmalıdır.")]
    string Password,

    [property: Required(ErrorMessage = "Şifre tekrar alanı zorunludur.")]
    [property: StringLength(128, MinimumLength = 8, ErrorMessage = "Şifre tekrarı 8 ile 128 karakter arasında olmalıdır.")]
    [property: Compare("Password", ErrorMessage = "Şifreler eşleşmiyor.")]
    string PasswordConfirm);
