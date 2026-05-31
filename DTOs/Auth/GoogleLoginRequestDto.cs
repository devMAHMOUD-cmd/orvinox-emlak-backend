using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Auth;

public sealed record GoogleLoginRequestDto(
    [property: Required(ErrorMessage = "Google ID token zorunludur.")]
    string IdToken);
