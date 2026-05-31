namespace CraftoraApi.DTOs.Auth;

public sealed record TokenDto(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn);
