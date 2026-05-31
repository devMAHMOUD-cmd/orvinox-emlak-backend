using CraftoraApi.DTOs.Auth;

namespace CraftoraApi.Services.Interfaces;

public interface IAuthService
{
    Task<string> RegisterAsync(RegisterDto dto);

    Task<string> VerifyEmailAsync(OtpDto dto);

    Task<TokenDto> LoginAsync(LoginDto dto);

    Task<TokenDto> GoogleLoginAsync(string idToken);

    Task<TokenDto> RefreshTokenAsync(string refreshToken);

    Task<bool> LogoutAsync(string refreshToken, string accessToken);

    Task<UserMeResponseDto> GetCurrentUserAsync(Guid userId);
}
