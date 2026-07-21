using CraftoraApi.Data;
using CraftoraApi.DTOs.Competition;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class CompetitionService : ICompetitionService
{
    private const string PublicAssetsBucketName = "public-assets";
    private const string PrivateProductsBucketName = "private-products";
    private const int PublicMediaUrlExpiryMinutes = 60;
    private const int CertificateUrlExpiryMinutes = 60 * 24 * 7;

    private readonly AppDbContext _dbContext;
    private readonly IStorageService _storageService;

    public CompetitionService(
        AppDbContext dbContext,
        IStorageService storageService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
    }

    public async Task<ActiveCompetitionDto> GetActiveCompetitionAsync(
        Guid? currentUserId,
        CancellationToken cancellationToken = default)
    {
        var contest = await GetActiveContestAsync(cancellationToken);
        return await MapToActiveDtoAsync(contest, currentUserId, cancellationToken);
    }

    public async Task<CompetitionLeaderboardResponseDto> GetActiveLeaderboardAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var contest = await GetActiveContestAsync(cancellationToken);
        return await BuildLeaderboardAsync(contest, page, pageSize, cancellationToken);
    }

    public async Task<ActiveCompetitionDto> JoinActiveCompetitionAsync(
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var contest = await GetActiveContestAsync(cancellationToken);
        var alreadyJoined = await _dbContext.ContestResults
            .AnyAsync(
                result => result.ContestId == contest.Id && result.UserId == currentUserId,
                cancellationToken);
        if (!alreadyJoined)
        {
            _dbContext.ContestResults.Add(new ContestResult
            {
                ContestId = contest.Id,
                UserId = currentUserId,
                TotalScore = 0,
                RewardClaimed = false,
                JoinedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return await MapToActiveDtoAsync(contest, currentUserId, cancellationToken);
    }

    public async Task<CompetitionHistoryDto> GetHistoryAsync(
        int months,
        CancellationToken cancellationToken = default)
    {
        var normalizedMonths = Math.Clamp(months, 1, 24);
        var now = DateTime.UtcNow;
        var from = now.AddMonths(-normalizedMonths);
        var contests = await _dbContext.Contests
            .AsNoTracking()
            .Where(contest => contest.EndDate >= from && contest.EndDate <= now)
            .OrderByDescending(contest => contest.EndDate)
            .ToListAsync(cancellationToken);

        var items = new List<CompetitionHistoryItemDto>();
        foreach (var contest in contests)
        {
            var winners = await BuildTopWinnersAsync(contest, cancellationToken);
            items.Add(new CompetitionHistoryItemDto(
                CompetitionId: contest.Id,
                Title: contest.Title,
                StartDate: contest.StartDate,
                EndDate: contest.EndDate,
                PrizePool: contest.RewardsHidden == true ? null : contest.PrizePool,
                RewardsHidden: contest.RewardsHidden == true,
                Winners: winners));
        }

        return new CompetitionHistoryDto(items);
    }

    public async Task<MyCompetitionHistoryDto> GetMyHistoryAsync(
        Guid currentUserId,
        int months,
        CancellationToken cancellationToken = default)
    {
        var normalizedMonths = Math.Clamp(months, 1, 24);
        var now = DateTime.UtcNow;
        var from = now.AddMonths(-normalizedMonths);

        var joinedResults = await _dbContext.ContestResults
            .AsNoTracking()
            .Include(result => result.Contest)
            .Where(result =>
                result.UserId == currentUserId &&
                result.Contest.EndDate >= from &&
                result.Contest.EndDate <= now)
            .OrderByDescending(result => result.Contest.EndDate)
            .ToListAsync(cancellationToken);

        if (joinedResults.Count == 0)
        {
            return new MyCompetitionHistoryDto(Array.Empty<MyCompetitionHistoryItemDto>());
        }

        var contestIds = joinedResults.Select(result => result.ContestId).ToList();
        var rewardsByContestId = await _dbContext.AdminCompetitionRewards
            .AsNoTracking()
            .Where(reward => reward.UserId == currentUserId && contestIds.Contains(reward.ContestId))
            .ToDictionaryAsync(reward => reward.ContestId, cancellationToken);

        var items = new List<MyCompetitionHistoryItemDto>(joinedResults.Count);
        foreach (var result in joinedResults)
        {
            var contest = result.Contest;
            var scoreRows = await BuildScoreRowsAsync(contest, cancellationToken);
            var scoreIndex = scoreRows.FindIndex(row => row.UserId == currentUserId);
            var scoreRow = scoreIndex >= 0 ? scoreRows[scoreIndex] : null;
            var score = scoreRow?.Score ?? 0m;
            rewardsByContestId.TryGetValue(contest.Id, out var reward);

            items.Add(new MyCompetitionHistoryItemDto(
                CompetitionId: contest.Id,
                Title: contest.Title,
                StartDate: contest.StartDate,
                EndDate: contest.EndDate,
                Status: GetCompetitionStatus(contest),
                IsJoined: true,
                Rank: result.FinalRank ?? (score == 0m ? null : scoreIndex + 1),
                Score: score,
                SalesPoints: scoreRow?.SalesPoints ?? 0m,
                ViewPoints: scoreRow?.ViewPoints ?? 0m,
                EngagementPoints: scoreRow?.EngagementPoints ?? 0m,
                LearningPoints: scoreRow?.LearningPoints ?? 0m,
                RewardType: reward?.RewardType,
                RewardAmount: reward?.Amount,
                RewardCurrency: reward?.Currency,
                CertificatePublicUrl: GenerateCertificateUrl(reward?.CertificateUrl)));
        }

        return new MyCompetitionHistoryDto(items);
    }

    public async Task<ActiveCompetitionDto> GetCompetitionAsync(
        Guid competitionId,
        Guid? currentUserId,
        CancellationToken cancellationToken = default)
    {
        var contest = await _dbContext.Contests
            .AsNoTracking()
            .FirstOrDefaultAsync(contest => contest.Id == competitionId, cancellationToken);

        if (contest is null)
        {
            throw new NotFoundException("Yarisma bulunamadi.");
        }

        return await MapToActiveDtoAsync(contest, currentUserId, cancellationToken);
    }

    private async Task<Contest> GetActiveContestAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var contest = await _dbContext.Contests
            .AsNoTracking()
            .Where(contest =>
                contest.IsActive == true &&
                contest.StartDate <= now &&
                contest.EndDate >= now)
            .OrderBy(contest => contest.EndDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (contest is null)
        {
            throw new NotFoundException("Aktif yarisma bulunamadi.");
        }

        return contest;
    }

    private async Task<ActiveCompetitionDto> MapToActiveDtoAsync(
        Contest contest,
        Guid? currentUserId,
        CancellationToken cancellationToken)
    {
        var totalParticipants = await _dbContext.ContestResults
            .AsNoTracking()
            .CountAsync(result => result.ContestId == contest.Id, cancellationToken);

        var isJoined = currentUserId.HasValue &&
            await _dbContext.ContestResults
                .AsNoTracking()
                .AnyAsync(
                    result => result.ContestId == contest.Id && result.UserId == currentUserId.Value,
                    cancellationToken);

        int? myRank = null;
        decimal? myScore = null;
        CompetitionPointsBreakdownDto? myBreakdown = null;
        if (currentUserId.HasValue && isJoined)
        {
            var rankings = await BuildScoreRowsAsync(contest, cancellationToken);
            var myIndex = rankings.FindIndex(row => row.UserId == currentUserId.Value);
            if (myIndex >= 0)
            {
                myRank = rankings[myIndex].Score == 0m ? null : myIndex + 1;
                myScore = rankings[myIndex].Score;
                myBreakdown = new CompetitionPointsBreakdownDto(
                    rankings[myIndex].SalesPoints,
                    rankings[myIndex].ViewPoints,
                    rankings[myIndex].EngagementPoints,
                    rankings[myIndex].LearningPoints);
            }
            else
            {
                myScore = 0m;
                myBreakdown = new CompetitionPointsBreakdownDto(0m, 0m, 0m, 0m);
            }
        }

        return new ActiveCompetitionDto(
            Id: contest.Id,
            Title: contest.Title,
            Description: contest.Description,
            StartDate: contest.StartDate,
            EndDate: contest.EndDate,
            RewardsHidden: contest.RewardsHidden == true,
            PrizePool: contest.RewardsHidden == true ? null : contest.PrizePool,
            IsJoined: isJoined,
            TotalParticipants: totalParticipants,
            MyRank: myRank,
            MyScore: myScore,
            MyBreakdown: myBreakdown);
    }

    private async Task<CompetitionLeaderboardResponseDto> BuildLeaderboardAsync(
        Contest contest,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var scoreRows = await BuildScoreRowsAsync(contest, cancellationToken);
        var totalCount = scoreRows.Count;
        var pageRows = scoreRows
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToList();
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);

        return new CompetitionLeaderboardResponseDto(
            CompetitionId: contest.Id,
            Items: await MapLeaderboardItemsAsync(pageRows, (normalizedPage - 1) * normalizedPageSize, cancellationToken),
            Page: normalizedPage,
            PageSize: normalizedPageSize,
            TotalCount: totalCount,
            TotalPages: totalPages);
    }

    private async Task<IReadOnlyList<CompetitionLeaderboardItemDto>> BuildTopWinnersAsync(
        Contest contest,
        CancellationToken cancellationToken)
    {
        var savedResults = await _dbContext.ContestResults
            .AsNoTracking()
            .Where(result => result.ContestId == contest.Id && result.FinalRank != null)
            .OrderBy(result => result.FinalRank)
            .Take(3)
            .Select(result => new CompetitionScoreRow(
                result.UserId,
                result.TotalScore ?? 0,
                0,
                0,
                0,
                0))
            .ToListAsync(cancellationToken);

        if (savedResults.Count > 0)
        {
            return await MapLeaderboardItemsAsync(savedResults, 0, cancellationToken);
        }

        var computed = (await BuildScoreRowsAsync(contest, cancellationToken))
            .Take(3)
            .ToList();

        return await MapLeaderboardItemsAsync(computed, 0, cancellationToken);
    }

    private async Task<List<CompetitionScoreRow>> BuildScoreRowsAsync(
        Contest contest,
        CancellationToken cancellationToken)
    {
        var scoreRows = await _dbContext.PointLogs
            .AsNoTracking()
            .Where(log =>
                log.CreatedAt >= contest.StartDate &&
                log.CreatedAt <= contest.EndDate &&
                _dbContext.ContestResults.Any(result =>
                    result.ContestId == contest.Id &&
                    result.UserId == log.UserId &&
                    log.CreatedAt >= (result.JoinedAt ?? contest.StartDate)))
            .GroupBy(log => log.UserId)
            .Select(group => new
            {
                UserId = group.Key,
                Score = group.Sum(log => log.PointsEarned),
                SalesPoints = group.Sum(log => log.ActionType == "make_sale" ? log.PointsEarned : 0m),
                ViewPoints = group.Sum(log => log.ActionType == "watch_reels" ? log.PointsEarned : 0m),
                EngagementPoints = group.Sum(log => log.ActionType == "receive_like" ? log.PointsEarned : 0m),
                LearningPoints = group.Sum(log => log.ActionType == "complete_lesson" ? log.PointsEarned : 0m)
            })
            .OrderByDescending(row => row.Score)
            .ThenBy(row => row.UserId)
            .ToListAsync(cancellationToken);

        return scoreRows
            .Select(row => new CompetitionScoreRow(
                row.UserId,
                row.Score,
                row.SalesPoints,
                row.ViewPoints,
                row.EngagementPoints,
                row.LearningPoints))
            .ToList();
    }

    private async Task<IReadOnlyList<CompetitionLeaderboardItemDto>> MapLeaderboardItemsAsync(
        IReadOnlyList<CompetitionScoreRow> rows,
        int rankOffset,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return Array.Empty<CompetitionLeaderboardItemDto>();
        }

        var userIds = rows.Select(row => row.UserId).ToList();
        var users = await _dbContext.Users
            .AsNoTracking()
            .Include(user => user.Shop)
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, cancellationToken);

        return rows
            .Select((row, index) =>
            {
                users.TryGetValue(row.UserId, out var user);
                var shop = user?.Shop?.IsActive == true ? user.Shop : null;

                return new CompetitionLeaderboardItemDto(
                    Rank: rankOffset + index + 1,
                    UserId: row.UserId,
                    ShopId: shop?.Id,
                    DisplayName: shop?.ShopName ?? user?.FullName ?? user?.Email,
                    AvatarUrl: user?.AvatarUrl,
                    LogoPublicUrl: GeneratePublicAssetUrl(shop?.LogoUrl ?? user?.AvatarUrl),
                    Score: row.Score,
                    SalesPoints: row.SalesPoints,
                    ViewPoints: row.ViewPoints,
                    EngagementPoints: row.EngagementPoints,
                    LearningPoints: row.LearningPoints,
                    Trend: null);
            })
            .ToList();
    }

    private string? GeneratePublicAssetUrl(string? objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return null;
        }

        return _storageService.GeneratePresignedDownloadUrl(
            PublicAssetsBucketName,
            objectKey,
            PublicMediaUrlExpiryMinutes);
    }

    private string? GenerateCertificateUrl(string? objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return null;
        }

        return _storageService.GeneratePresignedDownloadUrl(
            PrivateProductsBucketName,
            objectKey,
            CertificateUrlExpiryMinutes);
    }

    private static string GetCompetitionStatus(Contest contest)
    {
        return contest.EndDate <= DateTime.UtcNow && contest.IsActive != true
            ? "finished"
            : contest.IsActive == true ? "active" : "draft";
    }

    private sealed record CompetitionScoreRow(
        Guid UserId,
        decimal Score,
        decimal SalesPoints,
        decimal ViewPoints,
        decimal EngagementPoints,
        decimal LearningPoints);
}
