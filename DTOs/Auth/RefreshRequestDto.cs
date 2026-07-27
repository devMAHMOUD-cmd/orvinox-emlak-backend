using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Auth;

public sealed record RefreshRequestDto(
    [property: Required(ErrorMessage = "Refresh token zorunludur.")]
    [property: StringLength(1024, ErrorMessage = "Refresh token en fazla 1024 karakter olabilir.")]
    string RefreshToken);
