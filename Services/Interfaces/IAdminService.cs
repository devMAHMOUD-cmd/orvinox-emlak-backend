using CraftoraApi.DTOs.Admin;

namespace CraftoraApi.Services.Interfaces;

public interface IAdminService
{
    Task<AdminOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default);

    Task<AdminFinanceOverviewDto> GetFinanceOverviewAsync(
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default);

    Task<AdminPagedResponseDto<AdminCommissionListItemDto>> GetCommissionsAsync(
        int page,
        int pageSize,
        DateTime? startDate,
        DateTime? endDate,
        string? query,
        CancellationToken cancellationToken = default);

    Task<AdminPagedResponseDto<AdminSubscriptionFinanceListItemDto>> GetSubscriptionFinanceAsync(
        int page,
        int pageSize,
        string? status,
        string? query,
        CancellationToken cancellationToken = default);

    Task<int> ReindexProductsAsync(Guid adminUserId, CancellationToken cancellationToken = default);

    Task<int> ReindexShopsAsync(Guid adminUserId, CancellationToken cancellationToken = default);

    Task<int> ReindexMediaAsync(Guid adminUserId, CancellationToken cancellationToken = default);

    Task<AdminPagedResponseDto<AdminUserListItemDto>> GetUsersAsync(
        string? query,
        string? role,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<AdminUserDetailDto> GetUserDetailAsync(Guid userId, CancellationToken cancellationToken = default);

    Task WarnUserAsync(Guid adminUserId, Guid userId, AdminWarnUserRequestDto dto, CancellationToken cancellationToken = default);
    Task LockUserAsync(Guid adminUserId, Guid userId, AdminLockUserRequestDto dto, CancellationToken cancellationToken = default);
    Task UnlockUserAsync(Guid adminUserId, Guid userId, CancellationToken cancellationToken = default);
    Task SuspendUserAsync(Guid adminUserId, Guid userId, AdminSuspendUserRequestDto dto, CancellationToken cancellationToken = default);
    Task RestoreUserAsync(Guid adminUserId, Guid userId, CancellationToken cancellationToken = default);
    Task SoftDeleteUserAsync(Guid adminUserId, Guid userId, CancellationToken cancellationToken = default);

    Task<AdminPagedResponseDto<AdminReportDto>> GetReportsAsync(string? status, string? type, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AdminReportTargetDto> GetReportTargetAsync(Guid adminUserId, Guid reportId, CancellationToken cancellationToken = default);
    Task WarnReportTargetAsync(Guid adminUserId, Guid reportId, AdminWarnUserRequestDto dto, CancellationToken cancellationToken = default);
    Task BlockReportTargetAsync(Guid adminUserId, Guid reportId, AdminBlockReportTargetRequestDto dto, CancellationToken cancellationToken = default);
    Task ResolveReportAsync(Guid adminUserId, Guid reportId, CancellationToken cancellationToken = default);
    Task RejectReportAsync(Guid adminUserId, Guid reportId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminCompetitionDto>> GetCompetitionsAsync(CancellationToken cancellationToken = default);
    Task<AdminCompetitionLeaderboardResponseDto> GetCompetitionLeaderboardAsync(Guid id, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AdminCompetitionParticipantsResponseDto> GetCompetitionParticipantsAsync(Guid id, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AdminCompetitionDto> CreateCompetitionAsync(Guid adminUserId, AdminUpsertCompetitionDto dto, CancellationToken cancellationToken = default);
    Task<AdminCompetitionDto> UpdateCompetitionAsync(Guid adminUserId, Guid id, AdminUpsertCompetitionDto dto, CancellationToken cancellationToken = default);
    Task StartCompetitionAsync(Guid adminUserId, Guid id, CancellationToken cancellationToken = default);
    Task FinishCompetitionAsync(Guid adminUserId, Guid id, CancellationToken cancellationToken = default);
    Task DistributeRewardsAsync(Guid adminUserId, Guid id, AdminDistributeRewardsRequestDto dto, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PulseNewsDto>> GetPulseNewsAsync(bool includeUnpublished, CancellationToken cancellationToken = default);
    Task<PulseNewsDto> CreatePulseNewsAsync(Guid adminUserId, UpsertPulseNewsDto dto, CancellationToken cancellationToken = default);
    Task<PulseNewsDto> UpdatePulseNewsAsync(Guid adminUserId, Guid id, UpsertPulseNewsDto dto, CancellationToken cancellationToken = default);
    Task DeletePulseNewsAsync(Guid adminUserId, Guid id, CancellationToken cancellationToken = default);

    Task<HomeCardsDto> GetHomeCardsAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task<HomeCardsDto> UpdateHomeCardsAsync(Guid adminUserId, HomeCardsDto dto, CancellationToken cancellationToken = default);

    Task<AdminPagedResponseDto<AdminAuditLogDto>> GetAuditLogsAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}
