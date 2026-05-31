namespace CraftoraApi.DTOs.Gamification;

public sealed record PointLogDto(
    Guid Id,
    string ActionType,
    decimal PointsEarned,
    Guid? ReferenceId,
    DateTime? CreatedAt);
