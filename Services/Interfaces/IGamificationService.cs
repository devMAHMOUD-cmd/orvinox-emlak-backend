using CraftoraApi.DTOs.Gamification;

namespace CraftoraApi.Services.Interfaces;

public interface IGamificationService
{
    Task<List<LeaderboardUserDto>> GetLeaderboardAsync(int top = 50);

    Task<WalletDto> GetMyWalletAsync(Guid userId);
}
