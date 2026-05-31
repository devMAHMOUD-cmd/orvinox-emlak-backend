namespace CraftoraApi.DTOs.Gamification;

public sealed record LeaderboardUserDto(
    int Rank,
    Guid UserId,
    string? FullName,
    string? AvatarUrl,
    decimal TotalPoints,
    int CurrentStreak);
