using CraftoraApi.Data;
using CraftoraApi.DTOs.Discovery;
using CraftoraApi.DTOs.Home;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Enums;
using CraftoraApi.Redis;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services.Discovery;

public sealed class DiscoveryFeedService : IDiscoveryFeedService
{
    private const string PublicAssetsBucketName = "public-assets";
    private const string PrivateProductsBucketName = "private-products";
    private const int PublicUrlExpiryMinutes = 60;
    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromMinutes(30);

    private readonly AppDbContext _dbContext;
    private readonly ICacheService _cacheService;
    private readonly IDiscoveryRankingService _rankingService;
    private readonly IDiscoveryFeedCursorService _cursorService;
    private readonly IDiscoveryTrackingTokenService _trackingTokenService;
    private readonly IStorageService _storageService;

    public DiscoveryFeedService(
        AppDbContext dbContext,
        ICacheService cacheService,
        IDiscoveryRankingService rankingService,
        IDiscoveryFeedCursorService cursorService,
        IDiscoveryTrackingTokenService trackingTokenService,
        IStorageService storageService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _rankingService = rankingService ?? throw new ArgumentNullException(nameof(rankingService));
        _cursorService = cursorService ?? throw new ArgumentNullException(nameof(cursorService));
        _trackingTokenService = trackingTokenService
            ?? throw new ArgumentNullException(nameof(trackingTokenService));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
    }

    public async Task<DiscoveryFeedResponseDto> GetFeedAsync(
        Guid userId,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new UnauthorizedException("Gecersiz kullanici token'i.");
        }

        if (pageSize is < 1 or > 30)
        {
            throw new BadRequestException(
                "Discovery feed pageSize 1 ile 30 arasinda olmalidir.");
        }

        var cacheKey = DiscoveryCacheKeys.MixedSnapshot(userId);
        var boostVersion = await _cacheService.GetAsync<long>(
            DiscoveryCacheKeys.BoostVersion,
            cancellationToken);
        DiscoveryFeedSnapshot snapshot;
        var offset = 0;

        if (string.IsNullOrWhiteSpace(cursor))
        {
            snapshot = await CreateSnapshotAsync(
                userId,
                boostVersion,
                cancellationToken);
            await _cacheService.SetAsync(
                cacheKey,
                snapshot,
                SnapshotTtl,
                cancellationToken);
        }
        else
        {
            if (!_cursorService.TryRead(cursor, userId, out var cursorContext))
            {
                throw new BadRequestException(
                    "Discovery cursor gecersiz veya suresi dolmus.");
            }

            snapshot = await _cacheService.GetAsync<DiscoveryFeedSnapshot>(
                    cacheKey,
                    cancellationToken)
                ?? throw new BadRequestException(
                    "Discovery oturumu suresi dolmus. Akisi yenileyin.");
            if (snapshot.UserId != userId ||
                snapshot.FeedSessionId != cursorContext.FeedSessionId ||
                snapshot.BoostVersion != boostVersion)
            {
                throw new BadRequestException(
                    "Discovery akisi yenilendi. Akisi bastan acin.");
            }

            offset = cursorContext.Offset;
        }

        if (offset > snapshot.Items.Count)
        {
            throw new BadRequestException("Discovery cursor konumu gecersiz.");
        }

        var pageCandidates = snapshot.Items
            .Skip(offset)
            .Take(pageSize)
            .ToList();
        var items = await HydrateAsync(
            userId,
            snapshot.FeedSessionId,
            offset,
            pageCandidates,
            cancellationToken);
        var nextOffset = offset + pageCandidates.Count;
        var hasMore = nextOffset < snapshot.Items.Count;
        var nextCursor = hasMore
            ? _cursorService.Issue(
                userId,
                snapshot.FeedSessionId,
                nextOffset,
                snapshot.ExpiresAt)
            : null;

        return new DiscoveryFeedResponseDto(
            DiscoveryCacheKeys.MixedRankingVersion,
            items,
            nextCursor,
            hasMore);
    }

    private async Task<DiscoveryFeedSnapshot> CreateSnapshotAsync(
        Guid userId,
        long boostVersion,
        CancellationToken cancellationToken)
    {
        var mediaIds = await _rankingService.GetPersonalizedMediaIdsAsync(
            userId,
            cancellationToken);
        var productIds = await _rankingService.GetPersonalizedProductIdsAsync(
            userId,
            "product",
            cancellationToken);
        var courseIds = await _rankingService.GetPersonalizedProductIdsAsync(
            userId,
            "course",
            cancellationToken);

        var mediaShops = await _dbContext.Media
            .AsNoTracking()
            .Where(item => mediaIds.Contains(item.Id))
            .Select(item => new { item.Id, item.ShopId })
            .ToDictionaryAsync(item => item.Id, item => item.ShopId, cancellationToken);
        var productShops = await _dbContext.Products
            .AsNoTracking()
            .Where(item => productIds.Contains(item.Id))
            .Select(item => new { item.Id, item.ShopId })
            .ToDictionaryAsync(item => item.Id, item => item.ShopId, cancellationToken);
        var courseShops = await _dbContext.Courses
            .AsNoTracking()
            .Where(item => courseIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Product.ShopId })
            .ToDictionaryAsync(item => item.Id, item => item.ShopId, cancellationToken);

        var media = ToCandidates("media", mediaIds, mediaShops);
        var products = ToCandidates("product", productIds, productShops);
        var courses = ToCandidates("course", courseIds, courseShops);
        var expiresAt = DateTimeOffset.UtcNow.Add(SnapshotTtl);

        var organic = DiscoveryFeedMixer.Mix(media, products, courses);
        var sponsorLimit = organic.Count / 10;
        var sponsored = sponsorLimit > 0
            ? await LoadSponsoredCandidatesAsync(
                userId,
                sponsorLimit,
                cancellationToken)
            : [];

        return new DiscoveryFeedSnapshot(
            userId,
            Guid.NewGuid(),
            expiresAt,
            boostVersion,
            DiscoveryFeedMixer.InsertSponsored(organic, sponsored).ToList());
    }

    private async Task<List<DiscoveryFeedItemDto>> HydrateAsync(
        Guid userId,
        Guid feedSessionId,
        int offset,
        IReadOnlyList<DiscoveryFeedCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var mediaIds = candidates
            .Where(item => item.ContentType == "media")
            .Select(item => item.ContentId)
            .ToList();
        var productIds = candidates
            .Where(item => item.ContentType == "product")
            .Select(item => item.ContentId)
            .ToList();
        var courseIds = candidates
            .Where(item => item.ContentType == "course")
            .Select(item => item.ContentId)
            .ToList();

        var mediaById = await _dbContext.Media
            .AsNoTracking()
            .Include(item => item.Shop)
            .Include(item => item.Product)
                .ThenInclude(product => product!.ProductImages)
            .Where(item => mediaIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var productsById = await _dbContext.Products
            .AsNoTracking()
            .Include(item => item.Shop)
            .Include(item => item.ProductImages)
            .Where(item => productIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var coursesById = await _dbContext.Courses
            .AsNoTracking()
            .Include(item => item.Product)
                .ThenInclude(product => product.Shop)
            .Include(item => item.Product)
                .ThenInclude(product => product.ProductImages)
            .Include(item => item.CourseSections)
                .ThenInclude(section => section.CourseLessons)
            .Where(item => courseIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var since = DateTime.UtcNow.AddDays(-7);
        var productViewCounts = await _dbContext.AnalyticsEvents
            .AsNoTracking()
            .Where(item =>
                item.ProductId.HasValue &&
                productIds.Contains(item.ProductId.Value) &&
                item.EventType == AnalyticsEventType.ProductView &&
                item.CreatedAt >= since)
            .GroupBy(item => item.ProductId!.Value)
            .Select(group => new { ProductId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.ProductId, item => item.Count, cancellationToken);

        var result = new List<DiscoveryFeedItemDto>(candidates.Count);
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var position = offset + index;
            var item = candidate.ContentType switch
            {
                "media" when mediaById.TryGetValue(candidate.ContentId, out var medium) =>
                    MapMedia(userId, feedSessionId, position, candidate, medium),
                "product" when productsById.TryGetValue(candidate.ContentId, out var product) =>
                    MapProduct(
                        userId,
                        feedSessionId,
                        position,
                        candidate,
                        product,
                        productViewCounts.GetValueOrDefault(product.Id)),
                "course" when coursesById.TryGetValue(candidate.ContentId, out var course) =>
                    MapCourse(userId, feedSessionId, position, candidate, course),
                _ => null
            };

            if (item is not null)
            {
                result.Add(item);
            }
        }

        return result;
    }

    private DiscoveryFeedItemDto MapMedia(
        Guid userId,
        Guid feedSessionId,
        int position,
        DiscoveryFeedCandidate candidate,
        Models.Entities.Medium medium)
    {
        var dto = new HomeReelDto(
            medium.Id,
            medium.ShopId,
            medium.Shop.ShopName,
            GeneratePublicAssetUrl(medium.Shop.LogoUrl),
            medium.ProductId,
            medium.Product?.Title,
            medium.VideoUrl,
            GeneratePrivateProductUrl(medium.VideoUrl),
            GeneratePublicAssetUrl(medium.ThumbnailUrl)
                ?? GeneratePublicAssetUrl(GetProductCoverObjectKey(medium.Product)),
            medium.Caption,
            medium.ViewCount ?? 0,
            medium.LikeCount ?? 0,
            medium.SaveCount ?? 0,
            medium.ShareCount ?? 0,
            medium.CommentCount ?? 0,
            medium.Hashtags ?? [],
            medium.CreatedAt,
            _trackingTokenService.Issue(
                userId,
                "media",
                medium.Id,
                medium.ShopId,
                feedSessionId,
                position,
                candidate.IsSponsored,
                candidate.BoostId));

        return new DiscoveryFeedItemDto(
            "media",
            medium.Id,
            position,
            candidate.IsSponsored,
            candidate.IsSponsored ? "Sponsorlu" : null,
            dto,
            null,
            null);
    }

    private DiscoveryFeedItemDto MapProduct(
        Guid userId,
        Guid feedSessionId,
        int position,
        DiscoveryFeedCandidate candidate,
        Models.Entities.Product product,
        int viewCount)
    {
        var dto = new HomeTrendingProductDto(
            product.Id,
            product.Title,
            product.Description ?? string.Empty,
            product.Price,
            product.OriginalPrice,
            product.Currency ?? "USD",
            GeneratePublicAssetUrl(GetProductCoverObjectKey(product)),
            product.RatingAverage,
            product.ReviewCount ?? 0,
            product.SalesCount ?? 0,
            viewCount,
            product.ShopId,
            product.Shop.ShopName,
            product.Shop.Slug,
            _trackingTokenService.Issue(
                userId,
                "product",
                product.Id,
                product.ShopId,
                feedSessionId,
                position,
                candidate.IsSponsored,
                candidate.BoostId));

        return new DiscoveryFeedItemDto(
            "product",
            product.Id,
            position,
            candidate.IsSponsored,
            candidate.IsSponsored ? "Sponsorlu" : null,
            null,
            dto,
            null);
    }

    private DiscoveryFeedItemDto MapCourse(
        Guid userId,
        Guid feedSessionId,
        int position,
        DiscoveryFeedCandidate candidate,
        Models.Entities.Course course)
    {
        var activeSections = course.CourseSections
            .Where(section => section.IsActive)
            .ToList();
        var activeLessonCount = activeSections
            .SelectMany(section => section.CourseLessons)
            .Count(lesson => lesson.IsActive);
        var dto = new HomeFeaturedCourseDto(
            course.Id,
            course.ProductId,
            course.Product.Title,
            course.Product.Description ?? string.Empty,
            course.Product.Price,
            course.Product.OriginalPrice,
            course.Product.Currency ?? "USD",
            GeneratePublicAssetUrl(GetProductCoverObjectKey(course.Product)),
            course.Level,
            course.TotalDurationInMinutes,
            activeLessonCount,
            activeSections.Count,
            course.Product.RatingAverage,
            course.Product.ReviewCount ?? 0,
            course.Product.SalesCount ?? 0,
            course.Product.ShopId,
            course.Product.Shop.ShopName,
            course.Product.Shop.Slug,
            GeneratePublicAssetUrl(course.Product.Shop.LogoUrl),
            _trackingTokenService.Issue(
                userId,
                "course",
                course.Id,
                course.Product.ShopId,
                feedSessionId,
                position,
                candidate.IsSponsored,
                candidate.BoostId));

        return new DiscoveryFeedItemDto(
            "course",
            course.Id,
            position,
            candidate.IsSponsored,
            candidate.IsSponsored ? "Sponsorlu" : null,
            null,
            null,
            dto);
    }

    private static List<DiscoveryFeedCandidate> ToCandidates(
        string contentType,
        IReadOnlyList<Guid> ids,
        IReadOnlyDictionary<Guid, Guid> shops)
    {
        return ids
            .Where(shops.ContainsKey)
            .Select(id => new DiscoveryFeedCandidate(contentType, id, shops[id]))
            .ToList();
    }

    private static string? GetProductCoverObjectKey(Models.Entities.Product? product)
    {
        return product?.CoverImageUrl
            ?? product?.ProductImages
                .OrderBy(image => image.SortOrder)
                .ThenBy(image => image.CreatedAt)
                .Select(image => image.ObjectKey)
                .FirstOrDefault();
    }

    private async Task<List<DiscoveryFeedCandidate>> LoadSponsoredCandidatesAsync(
        Guid userId,
        int limit,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere)
        {
            await _dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT boost_id, content_type, content_id, shop_id
                FROM public.get_sponsored_discovery_candidates(
                    CAST(@user_id AS uuid),
                    CAST(@candidate_limit AS integer))
                """;
            AddParameter(command, "user_id", userId);
            AddParameter(command, "candidate_limit", limit);

            var candidates = new List<DiscoveryFeedCandidate>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add(new DiscoveryFeedCandidate(
                    reader.GetString(1),
                    reader.GetGuid(2),
                    reader.GetGuid(3),
                    IsSponsored: true,
                    BoostId: reader.GetGuid(0)));
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

    private string? GeneratePublicAssetUrl(string? objectKey)
    {
        return string.IsNullOrWhiteSpace(objectKey)
            ? null
            : _storageService.GeneratePresignedDownloadUrl(
                PublicAssetsBucketName,
                objectKey,
                PublicUrlExpiryMinutes);
    }

    private string? GeneratePrivateProductUrl(string? objectKey)
    {
        return string.IsNullOrWhiteSpace(objectKey)
            ? null
            : _storageService.GeneratePresignedDownloadUrl(
                PrivateProductsBucketName,
                objectKey,
                PublicUrlExpiryMinutes);
    }
}

public sealed record DiscoveryFeedSnapshot(
    Guid UserId,
    Guid FeedSessionId,
    DateTimeOffset ExpiresAt,
    long BoostVersion,
    List<DiscoveryFeedCandidate> Items);
