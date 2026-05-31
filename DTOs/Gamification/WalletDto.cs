namespace CraftoraApi.DTOs.Gamification;

public sealed record WalletDto(
    decimal TotalPoints,
    int CurrentRank,
    List<PointLogDto> PointLogs);
