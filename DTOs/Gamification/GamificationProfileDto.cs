namespace CraftoraApi.DTOs.Gamification;

public sealed record GamificationProfileDto(
    Guid UserId,
    decimal TotalXp,
    int Level,
    decimal NextLevelXp,
    decimal CurrentLevelXp,
    int? ActiveCompetitionRank,
    decimal? ActiveCompetitionScore,
    XpBreakdownDto Breakdown);

public sealed record XpBreakdownDto(
    decimal SalesPoints,
    decimal ViewPoints,
    decimal EngagementPoints,
    decimal LearningPoints,
    decimal ProductPoints,
    decimal PurchasePoints);
