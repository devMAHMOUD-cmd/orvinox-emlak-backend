namespace CraftoraApi.DTOs.Auth;

public sealed record SendOtpEmailEvent(
    string Email,
    string OtpCode,
    string FullName);
