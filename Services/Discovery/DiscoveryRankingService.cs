using System.Data;
using CraftoraApi.Data;
using CraftoraApi.Redis;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services.Discovery;

public sealed class DiscoveryRankingService : IDiscoveryRankingService
{
    public const string CurrentRankingVersion = "reels-organic-v1";
    private const int CandidateLimit = 500;
    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _dbContext;
    private readonly ICacheService _cacheService;

    public DiscoveryRankingService(
        AppDbContext dbContext,
        ICacheService cacheService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
    }

    public async Task<IReadOnlyList<Guid>> GetPersonalizedMediaIdsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Discovery user id cannot be empty.", nameof(userId));
        }

        var cacheKey = GetSnapshotCacheKey(userId);
        var cachedIds = await _cacheService.GetAsync<List<Guid>>(
            cacheKey,
            cancellationToken);
        if (cachedIds is not null)
        {
            return cachedIds;
        }

        var candidates = await LoadCandidatesAsync(userId, cancellationToken);
        var rankedIds = DiscoveryRankingDiversifier
            .Diversify(candidates)
            .ToList();

        await _cacheService.SetAsync(
            cacheKey,
            rankedIds,
            SnapshotTtl,
            cancellationToken);
        return rankedIds;
    }

    public Task InvalidateMediaSnapshotAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Discovery user id cannot be empty.", nameof(userId));
        }

        return _cacheService.RemoveAsync(
            GetSnapshotCacheKey(userId),
            cancellationToken);
    }

    private async Task<List<DiscoveryRankedMediaCandidate>> LoadCandidatesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await _dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT media_id, shop_id, ranking_score, ranking_reason
                FROM public.get_personalized_media_candidates(
                    CAST(@user_id AS uuid),
                    CAST(@candidate_limit AS integer))
                """;

            var userParameter = command.CreateParameter();
            userParameter.ParameterName = "user_id";
            userParameter.Value = userId;
            command.Parameters.Add(userParameter);

            var limitParameter = command.CreateParameter();
            limitParameter.ParameterName = "candidate_limit";
            limitParameter.Value = CandidateLimit;
            command.Parameters.Add(limitParameter);

            var candidates = new List<DiscoveryRankedMediaCandidate>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add(new DiscoveryRankedMediaCandidate(
                    MediaId: reader.GetGuid(0),
                    ShopId: reader.GetGuid(1),
                    Score: reader.GetDecimal(2),
                    Reason: reader.GetString(3)));
            }

            return candidates;
        }
        finally
        {
            if (openedHere)
            {
                await _dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    private static string GetSnapshotCacheKey(Guid userId)
    {
        return $"discovery:reels:snapshot:{CurrentRankingVersion}:user:{userId:D}";
    }
}
