using CraftoraApi.Data;
using CraftoraApi.DTOs.Gamification;
using CraftoraApi.Models.Entities;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class GamificationService : IGamificationService
{
    private readonly AppDbContext _dbContext;

    public GamificationService(AppDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<List<LeaderboardUserDto>> GetLeaderboardAsync(int top = 50)
    {
        var take = Math.Clamp(top, 1, 100);
        var users = await _dbContext.UserPoints
            .AsNoTracking()
            .Include(point => point.User)
            .OrderByDescending(point => point.TotalPoints ?? 0)
            .ThenBy(point => point.UpdatedAt)
            .Take(take)
            .ToListAsync();

        return users
            .Select((userPoint, index) => new LeaderboardUserDto(
                Rank: index + 1,
                UserId: userPoint.UserId,
                FullName: userPoint.User.FullName,
                AvatarUrl: userPoint.User.AvatarUrl,
                TotalPoints: userPoint.TotalPoints ?? 0,
                CurrentStreak: userPoint.CurrentStreak ?? 0))
            .ToList();
    }

    public async Task<WalletDto> GetMyWalletAsync(Guid userId)
    {
        var userPoint = await _dbContext.UserPoints
            .AsNoTracking()
            .FirstOrDefaultAsync(point => point.UserId == userId);

        var pointLogs = await _dbContext.PointLogs
            .AsNoTracking()
            .Where(log => log.UserId == userId)
            .OrderByDescending(log => log.CreatedAt)
            .Take(100)
            .ToListAsync();

        return new WalletDto(
            TotalPoints: userPoint?.TotalPoints ?? 0,
            CurrentRank: userPoint?.CurrentRank ?? 0,
            PointLogs: pointLogs.Select(MapToPointLogDto).ToList());
    }

    public async Task<GamificationProfileDto> GetProfileAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var totalXp = await _dbContext.UserPoints
            .AsNoTracking()
            .Where(point => point.UserId == userId)
            .Select(point => point.TotalPoints ?? 0)
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var activeContest = await _dbContext.Contests
            .AsNoTracking()
            .Where(contest =>
                contest.IsActive == true &&
                contest.StartDate <= now &&
                contest.EndDate >= now)
            .OrderBy(contest => contest.EndDate)
            .FirstOrDefaultAsync(cancellationToken);

        int? activeRank = null;
        decimal? activeScore = null;
        if (activeContest is not null)
        {
            var rankedScores = await _dbContext.PointLogs
                .AsNoTracking()
                .Where(log =>
                    log.CreatedAt >= activeContest.StartDate &&
                    log.CreatedAt <= activeContest.EndDate &&
                    _dbContext.ContestResults.Any(result =>
                        result.ContestId == activeContest.Id &&
                        result.UserId == log.UserId &&
                        log.CreatedAt >= (result.JoinedAt ?? activeContest.StartDate)))
                .GroupBy(log => log.UserId)
                .Select(group => new
                {
                    UserId = group.Key,
                    Score = group.Sum(log => log.PointsEarned)
                })
                .OrderByDescending(row => row.Score)
                .ThenBy(row => row.UserId)
                .ToListAsync(cancellationToken);

            var index = rankedScores.FindIndex(row => row.UserId == userId);
            if (index >= 0)
            {
                activeRank = index + 1;
                activeScore = rankedScores[index].Score;
            }
        }

        var breakdownRows = await _dbContext.PointLogs
            .AsNoTracking()
            .Where(log => log.UserId == userId)
            .GroupBy(log => log.ActionType)
            .Select(group => new
            {
                ActionType = group.Key,
                Points = group.Sum(log => log.PointsEarned)
            })
            .ToListAsync(cancellationToken);

        var breakdown = breakdownRows.ToDictionary(
            row => row.ActionType,
            row => row.Points,
            StringComparer.OrdinalIgnoreCase);

        var level = CalculateLevel(totalXp);
        var currentLevelXp = CalculateLevelStartXp(level);
        var nextLevelXp = CalculateLevelStartXp(level + 1);

        return new GamificationProfileDto(
            UserId: userId,
            TotalXp: totalXp,
            Level: level,
            NextLevelXp: nextLevelXp,
            CurrentLevelXp: currentLevelXp,
            ActiveCompetitionRank: activeRank,
            ActiveCompetitionScore: activeScore,
            Breakdown: new XpBreakdownDto(
                SalesPoints: breakdown.GetValueOrDefault("make_sale"),
                ViewPoints: breakdown.GetValueOrDefault("watch_reels"),
                EngagementPoints: breakdown.GetValueOrDefault("receive_like"),
                LearningPoints: breakdown.GetValueOrDefault("complete_lesson"),
                ProductPoints: breakdown.GetValueOrDefault("create_product"),
                PurchasePoints: breakdown.GetValueOrDefault("purchase_product")));
    }

    public async Task AwardPointsAsync(
        Guid userId,
        string actionType,
        decimal points,
        Guid? referenceId = null,
        bool preventDuplicate = false,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var inserted = preventDuplicate && referenceId.HasValue
            ? await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO point_logs (user_id, action_type, points_earned, reference_id, created_at)
                VALUES ({userId}, {actionType}, {points}, {referenceId.Value}, {DateTime.UtcNow})
                ON CONFLICT DO NOTHING
                """,
                cancellationToken)
            : await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO point_logs (user_id, action_type, points_earned, reference_id, created_at)
                VALUES ({userId}, {actionType}, {points}, {referenceId}, {DateTime.UtcNow})
                """,
                cancellationToken);

        if (inserted == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO user_points (user_id, total_points, current_rank, current_streak, updated_at)
            VALUES ({userId}, {points}, 0, 0, {DateTime.UtcNow})
            ON CONFLICT (user_id) DO UPDATE
            SET total_points = user_points.total_points + EXCLUDED.total_points,
                updated_at = EXCLUDED.updated_at
            """,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static PointLogDto MapToPointLogDto(PointLog log)
    {
        return new PointLogDto(
            Id: log.Id,
            ActionType: log.ActionType,
            PointsEarned: log.PointsEarned,
            ReferenceId: log.ReferenceId,
            CreatedAt: log.CreatedAt);
    }

    private static int CalculateLevel(decimal totalXp)
    {
        return Math.Max(1, (int)Math.Floor(Math.Sqrt((double)Math.Max(totalXp, 0) / 100)) + 1);
    }

    private static decimal CalculateLevelStartXp(int level)
    {
        var normalizedLevel = Math.Max(level, 1);
        return (normalizedLevel - 1) * (normalizedLevel - 1) * 100;
    }
}
