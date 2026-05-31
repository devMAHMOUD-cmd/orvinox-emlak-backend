using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Auth;

public sealed record RefreshRequestDto(
    [property: Required(ErrorMessage = "Refresh token zorunludur.")]
    string RefreshToken);
