using CraftoraApi.DTOs.Competition;

namespace CraftoraApi.Services.Interfaces;

public interface ICompetitionService
{
    Task<ActiveCompetitionDto> GetActiveCompetitionAsync(
        Guid? currentUserId,
        CancellationToken cancellationToken = default);

    Task<CompetitionLeaderboardResponseDto> GetActiveLeaderboardAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<ActiveCompetitionDto> JoinActiveCompetitionAsync(
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<CompetitionHistoryDto> GetHistoryAsync(
        int months,
        CancellationToken cancellationToken = default);

    Task<ActiveCompetitionDto> GetCompetitionAsync(
        Guid competitionId,
        Guid? currentUserId,
        CancellationToken cancellationToken = default);
}
