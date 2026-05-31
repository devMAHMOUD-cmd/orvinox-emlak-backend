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

    private static PointLogDto MapToPointLogDto(PointLog log)
    {
        return new PointLogDto(
            Id: log.Id,
            ActionType: log.ActionType,
            PointsEarned: log.PointsEarned,
            ReferenceId: log.ReferenceId,
            CreatedAt: log.CreatedAt);
    }
}
