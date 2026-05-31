using CraftoraApi.DTOs.Auth;
using CraftoraApi.Models.Entities;

namespace CraftoraApi.Services.Interfaces;

public interface IJwtProvider
{
    TokenDto GenerateTokens(User user);
}
