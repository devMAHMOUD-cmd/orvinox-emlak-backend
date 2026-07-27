using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Auth;

public sealed record GoogleLoginRequestDto(
    [property: Required(ErrorMessage = "Google ID token zorunludur.")]
    [property: StringLength(16384, ErrorMessage = "Google ID token en fazla 16384 karakter olabilir.")]
    string IdToken);
