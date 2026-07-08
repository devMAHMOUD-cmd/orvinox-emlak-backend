namespace CraftoraApi.DTOs.Competition;

public sealed record ActiveCompetitionDto(
    Guid Id,
    string Title,
    string? Description,
    DateTime StartDate,
    DateTime EndDate,
    bool RewardsHidden,
    string? PrizePool,
    bool IsJoined,
    int TotalParticipants,
    int? MyRank,
    decimal? MyScore);

public sealed record CompetitionLeaderboardResponseDto(
    Guid CompetitionId,
    IReadOnlyList<CompetitionLeaderboardItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record CompetitionLeaderboardItemDto(
    int Rank,
    Guid UserId,
    Guid? ShopId,
    string? DisplayName,
    string? AvatarUrl,
    string? LogoPublicUrl,
    decimal Score,
    decimal SalesPoints,
    decimal ViewPoints,
    decimal EngagementPoints,
    decimal LearningPoints,
    string? Trend);

public sealed record CompetitionHistoryDto(
    IReadOnlyList<CompetitionHistoryItemDto> Items);

public sealed record CompetitionHistoryItemDto(
    Guid CompetitionId,
    string Title,
    DateTime StartDate,
    DateTime EndDate,
    string? PrizePool,
    bool RewardsHidden,
    IReadOnlyList<CompetitionLeaderboardItemDto> Winners);
