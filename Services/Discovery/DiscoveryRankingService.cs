using System.Data;
using CraftoraApi.Data;
using CraftoraApi.Redis;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services.Discovery;

public sealed class DiscoveryRankingService : IDiscoveryRankingService
{
    public const string CurrentRankingVersion = DiscoveryCacheKeys.ReelsRankingVersion;
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

    public async Task<IReadOnlyList<Guid>> GetPersonalizedProductIdsAsync(
        Guid userId,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var normalizedType = contentType?.Trim().ToLowerInvariant();
        if (userId == Guid.Empty || normalizedType is not ("product" or "course"))
        {
            throw new ArgumentException("Discovery product ranking request is invalid.");
        }

        var cacheKey = GetProductSnapshotCacheKey(userId, normalizedType);
        var cachedIds = await _cacheService.GetAsync<List<Guid>>(cacheKey, cancellationToken);
        if (cachedIds is not null)
        {
            return cachedIds;
        }

        var ids = await LoadProductCandidatesAsync(
            userId,
            normalizedType,
            cancellationToken);
        await _cacheService.SetAsync(cacheKey, ids, SnapshotTtl, cancellationToken);
        return ids;
    }

    public Task InvalidateSnapshotsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Discovery user id cannot be empty.", nameof(userId));
        }

        return Task.WhenAll(
            _cacheService.RemoveAsync(
                DiscoveryCacheKeys.ReelsSnapshot(userId),
                cancellationToken),
            _cacheService.RemoveAsync(
                DiscoveryCacheKeys.ProductSnapshot(userId, "product"),
                cancellationToken),
            _cacheService.RemoveAsync(
                DiscoveryCacheKeys.ProductSnapshot(userId, "course"),
                cancellationToken),
            _cacheService.RemoveAsync(
                DiscoveryCacheKeys.MixedSnapshot(userId),
                cancellationToken));
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

    private async Task<List<Guid>> LoadProductCandidatesAsync(
        Guid userId,
        string contentType,
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
                SELECT content_id
                FROM public.get_personalized_product_candidates(
                    CAST(@user_id AS uuid),
                    CAST(@content_type AS text),
                    CAST(@candidate_limit AS integer))
                """;

            AddParameter(command, "user_id", userId);
            AddParameter(command, "content_type", contentType);
            AddParameter(command, "candidate_limit", CandidateLimit);

            var ids = new List<Guid>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(reader.GetGuid(0));
            }

            return ids;
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
        return DiscoveryCacheKeys.ReelsSnapshot(userId);
    }

    private static string GetProductSnapshotCacheKey(Guid userId, string contentType)
    {
        return DiscoveryCacheKeys.ProductSnapshot(userId, contentType);
    }

    private static void AddParameter(
        System.Data.Common.DbCommand command,
        string name,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
