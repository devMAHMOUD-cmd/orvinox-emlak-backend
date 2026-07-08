using CraftoraApi.DTOs.Gamification;

namespace CraftoraApi.Services.Interfaces;

public interface IGamificationService
{
    Task<List<LeaderboardUserDto>> GetLeaderboardAsync(int top = 50);

    Task<WalletDto> GetMyWalletAsync(Guid userId);

    Task<GamificationProfileDto> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);

    Task AwardPointsAsync(
        Guid userId,
        string actionType,
        decimal points,
        Guid? referenceId = null,
        bool preventDuplicate = false,
        CancellationToken cancellationToken = default);
}
