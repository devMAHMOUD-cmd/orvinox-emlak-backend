using System.Data;
using System.Data.Common;
using CraftoraApi.Data;
using CraftoraApi.DTOs.Media;
using CraftoraApi.DTOs.Notification;
using CraftoraApi.Infrastructure.Messaging;
using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.Infrastructure.Security;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Elasticsearch;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Redis;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class MediaService : IMediaService
{
    private const string MediaTargetType = "Media";
    private const string PublicAssetsBucketName = "public-assets";
    private const string PrivateProductsBucketName = "private-products";
    private const string TrackedViewsSetKey = "media:tracked-views";
    private const string FeedCacheVersionKey = "media:feed:contract:v2:version";
    private const string LikedMediaCacheVersionKey = "media:liked:contract:v1:version";
    private const int PublicUrlExpiryMinutes = 60;
    private static readonly TimeSpan FeedCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ViewCountCacheTtl = TimeSpan.FromHours(24);

    private readonly AppDbContext _dbContext;
    private readonly IRabbitMqPublisher _rabbitMqPublisher;
    private readonly ICacheService _cacheService;
    private readonly IStorageService _storageService;
    private readonly IUploadService _uploadService;
    private readonly INotificationService _notificationService;
    private readonly IAnalyticsEventService _analyticsEventService;
    private readonly IDiscoveryTrackingTokenService _discoveryTrackingTokenService;
    private readonly IDiscoveryRankingService _discoveryRankingService;

    public MediaService(
        AppDbContext dbContext,
        IRabbitMqPublisher rabbitMqPublisher,
        ICacheService cacheService,
        IStorageService storageService,
        IUploadService uploadService,
        INotificationService notificationService,
        IAnalyticsEventService analyticsEventService,
        IDiscoveryTrackingTokenService discoveryTrackingTokenService,
        IDiscoveryRankingService discoveryRankingService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _rabbitMqPublisher = rabbitMqPublisher ?? throw new ArgumentNullException(nameof(rabbitMqPublisher));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _uploadService = uploadService ?? throw new ArgumentNullException(nameof(uploadService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _analyticsEventService = analyticsEventService ?? throw new ArgumentNullException(nameof(analyticsEventService));
        _discoveryTrackingTokenService = discoveryTrackingTokenService
            ?? throw new ArgumentNullException(nameof(discoveryTrackingTokenService));
        _discoveryRankingService = discoveryRankingService
            ?? throw new ArgumentNullException(nameof(discoveryRankingService));
    }

    public async Task<List<MediaResponseDto>> GetFeedAsync(Guid? currentUserId, int page = 1, int pageSize = 10)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 50);

        if (currentUserId.HasValue)
        {
            return await GetPersonalizedFeedAsync(
                currentUserId.Value,
                normalizedPage,
                normalizedPageSize);
        }

        var cacheKey = await GetFeedCacheKeyAsync(normalizedPage, normalizedPageSize);

        var cachedFeed = await _cacheService.GetAsync<List<MediaResponseDto>>(cacheKey);
        if (cachedFeed is not null)
        {
            var activeShopFeed = await FilterToActiveShopMediaAsync(cachedFeed);
            if (activeShopFeed.Count != cachedFeed.Count)
            {
                await _cacheService.RemoveAsync(cacheKey);
            }

            var feedWithState = await ApplyUserMediaStateAsync(activeShopFeed, currentUserId);
            return ApplyDiscoveryTrackingTokens(
                feedWithState,
                currentUserId,
                normalizedPage,
                normalizedPageSize);
        }

        var media = await _dbContext.Media
            .AsNoTracking()
            .Include(item => item.Shop)
            .Include(item => item.Product)
            .Where(item =>
                item.IsActive == true &&
                item.Status == MediaStatus.Ready &&
                item.Shop.IsActive == true)
            .OrderByDescending(item => item.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();

        var anonymousFeed = media.Select(MapToResponse).ToList();
        await _cacheService.SetAsync(cacheKey, anonymousFeed, FeedCacheTtl);

        var response = await ApplyUserMediaStateAsync(anonymousFeed, currentUserId);
        return ApplyDiscoveryTrackingTokens(
            response,
            currentUserId,
            normalizedPage,
            normalizedPageSize);
    }

    private async Task<List<MediaResponseDto>> GetPersonalizedFeedAsync(
        Guid userId,
        int page,
        int pageSize)
    {
        var rankedIds = await _discoveryRankingService
            .GetPersonalizedMediaIdsAsync(userId);
        var pageIds = rankedIds
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        if (pageIds.Count == 0)
        {
            return [];
        }

        var media = await _dbContext.Media
            .AsNoTracking()
            .Include(item => item.Shop)
            .Include(item => item.Product)
            .Where(item => pageIds.Contains(item.Id))
            .ToListAsync();
        var mediaById = media.ToDictionary(item => item.Id);
        var response = pageIds
            .Where(mediaById.ContainsKey)
            .Select(mediaId => MapToResponse(mediaById[mediaId]))
            .ToList();
        var responseWithState = await ApplyUserMediaStateAsync(response, userId);

        return ApplyDiscoveryTrackingTokens(
            responseWithState,
            userId,
            page,
            pageSize);
    }

    public async Task<MediaResponseDto> GetMediaByIdAsync(Guid mediaId, Guid? currentUserId)
    {
        var media = await _dbContext.Media
            .AsNoTracking()
            .Include(item => item.Shop)
            .Include(item => item.Product)
            .FirstOrDefaultAsync(item =>
                item.Id == mediaId &&
                item.IsActive == true &&
                item.Status == MediaStatus.Ready &&
                item.Shop.IsActive == true);

        if (media is null)
        {
            throw new NotFoundException("Medya bulunamadi.");
        }

        var response = MapToResponse(media);
        return (await ApplyUserMediaStateAsync([response], currentUserId))[0];
    }

    public async Task<MediaLikeListResponseDto> GetMediaLikesAsync(
        Guid mediaId,
        int page = 1,
        int pageSize = 30)
    {
        _ = await GetActiveMediaAsync(mediaId);

        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 50);
        var likesQuery = _dbContext.MediaLikes
            .AsNoTracking()
            .Where(like =>
                like.MediaId == mediaId &&
                like.User.IsActive == true &&
                like.User.DeletedAt == null)
            .OrderByDescending(like => like.CreatedAt);

        var totalCount = await likesQuery.CountAsync();
        var likes = await likesQuery
            .Include(like => like.User)
                .ThenInclude(user => user.Shop)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();

        var items = likes
            .Select(like =>
            {
                var user = like.User;
                var shop = user.Shop;

                return new MediaLikeUserDto(
                    UserId: user.Id,
                    FullName: user.FullName,
                    AvatarPublicUrl: GenerateStoragePublicUrl(user.AvatarUrl, PublicAssetsBucketName),
                    ShopId: shop?.Id,
                    ShopName: shop?.ShopName,
                    ShopSlug: shop?.Slug,
                    ShopLogoPublicUrl: GenerateStoragePublicUrl(shop?.LogoUrl, PublicAssetsBucketName),
                    IsShopVerified: shop?.IsVerified == true,
                    LikedAt: like.CreatedAt);
            })
            .ToList();
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);

        return new MediaLikeListResponseDto(
            Items: items,
            Page: normalizedPage,
            PageSize: normalizedPageSize,
            TotalCount: totalCount,
            TotalPages: totalPages);
    }

    public async Task<List<MediaResponseDto>> GetSavedMediaAsync(Guid userId, int page = 1, int pageSize = 12)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 50);

        var mediaIds = await _dbContext.MediaSaves
            .AsNoTracking()
            .Where(save => save.UserId == userId)
            .OrderByDescending(save => save.CreatedAt)
            .Select(save => save.MediaId)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();

        if (mediaIds.Count == 0)
        {
            return new List<MediaResponseDto>();
        }

        var media = await _dbContext.Media
            .AsNoTracking()
            .Include(item => item.Shop)
            .Include(item => item.Product)
            .Where(item =>
                mediaIds.Contains(item.Id) &&
                item.IsActive == true &&
                item.Status == MediaStatus.Ready &&
                item.Shop.IsActive == true)
            .ToListAsync();

        var mediaById = media.ToDictionary(item => item.Id);
        var response = mediaIds
            .Where(mediaById.ContainsKey)
            .Select(mediaId => MapToResponse(mediaById[mediaId]))
            .ToList();
        return await ApplyUserMediaStateAsync(response, userId);
    }

    public async Task<List<MediaResponseDto>> GetLikedMediaAsync(Guid userId, int page = 1, int pageSize = 12)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 50);
        var cacheKey = await GetLikedMediaCacheKeyAsync(userId, normalizedPage, normalizedPageSize);

        var cachedMedia = await _cacheService.GetAsync<List<MediaResponseDto>>(cacheKey);
        if (cachedMedia is not null)
        {
            var activeShopMedia = await FilterToActiveShopMediaAsync(cachedMedia);
            if (activeShopMedia.Count != cachedMedia.Count)
            {
                await _cacheService.RemoveAsync(cacheKey);
            }

            return await ApplyUserMediaStateAsync(activeShopMedia, userId);
        }

        var mediaIds = await _dbContext.MediaLikes
            .AsNoTracking()
            .Where(like => like.UserId == userId)
            .OrderByDescending(like => like.CreatedAt)
            .Select(like => like.MediaId)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();

        if (mediaIds.Count == 0)
        {
            return new List<MediaResponseDto>();
        }

        var media = await _dbContext.Media
            .AsNoTracking()
            .Include(item => item.Shop)
            .Include(item => item.Product)
            .Where(item =>
                mediaIds.Contains(item.Id) &&
                item.IsActive == true &&
                item.Status == MediaStatus.Ready &&
                item.Shop.IsActive == true)
            .ToListAsync();

        var mediaById = media.ToDictionary(item => item.Id);
        var response = mediaIds
            .Where(mediaById.ContainsKey)
            .Select(mediaId => MapToResponse(mediaById[mediaId]))
            .ToList();

        await _cacheService.SetAsync(cacheKey, response, FeedCacheTtl);
        return await ApplyUserMediaStateAsync(response, userId);
    }

    public async Task<List<MediaResponseDto>> GetShopMediaAsync(Guid shopId, int page = 1, int pageSize = 10)
    {
        var shopExists = await _dbContext.Shops.AnyAsync(shop =>
            shop.Id == shopId &&
            shop.IsActive == true);

        if (!shopExists)
        {
            throw new NotFoundException("Mağaza bulunamadı.");
        }

        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 50);

        var media = await _dbContext.Media
            .AsNoTracking()
            .Include(item => item.Shop)
            .Include(item => item.Product)
            .Where(item =>
                item.ShopId == shopId &&
                item.IsActive == true &&
                item.Status == MediaStatus.Ready &&
                item.Shop.IsActive == true)
            .OrderByDescending(item => item.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();

        return media.Select(MapToResponse).ToList();
    }

    public async Task<List<MediaResponseDto>> GetMyMediaAsync(Guid userId, int page = 1, int pageSize = 12)
    {
        var shop = await _dbContext.Shops
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == userId);

        if (shop is null)
        {
            throw new NotFoundException("Magaza bulunamadi.");
        }

        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 50);
        var media = await _dbContext.Media
            .AsNoTracking()
            .Include(item => item.Shop)
            .Include(item => item.Product)
            .Where(item =>
                item.ShopId == shop.Id &&
                item.IsActive == true)
            .OrderByDescending(item => item.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();

        return media.Select(MapToResponse).ToList();
    }

    public async Task<MediaResponseDto> UploadMediaAsync(Guid userId, UploadMediaDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var shop = await _dbContext.Shops.FirstOrDefaultAsync(shop =>
            shop.UserId == userId &&
            shop.IsActive == true);

        if (shop is null)
        {
            throw new BadRequestException("Medya yüklemek için aktif bir mağazanız olmalıdır.");
        }

        var product = await _dbContext.Products.FirstOrDefaultAsync(product =>
            product.Id == dto.ProductId &&
            product.ShopId == shop.Id &&
            product.IsActive == true);

        if (product is null)
        {
            throw new NotFoundException("Ürün bulunamadı veya bu mağazaya ait değil.");
        }

        await _uploadService.ValidateMediaVideoAsync(userId, dto.OriginalFileUrl);
        if (!string.IsNullOrWhiteSpace(dto.ThumbnailUrl))
        {
            await _uploadService.ValidateMediaThumbnailAsync(userId, dto.ThumbnailUrl);
        }

        var media = new Medium
        {
            ShopId = shop.Id,
            ProductId = dto.ProductId,
            VideoUrl = dto.OriginalFileUrl,
            ThumbnailUrl = dto.ThumbnailUrl,
            Caption = dto.Caption,
            Hashtags = NormalizeHashtags(dto.Hashtags),
            ViewCount = 0,
            LikeCount = 0,
            SaveCount = 0,
            ShareCount = 0,
            CommentCount = 0,
            Status = MediaStatus.Processing,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Media.Add(media);
        await _dbContext.SaveChangesAsync();
        await InvalidateFeedCacheAsync();
        await PublishMediaIndexMessageAsync(media.Id);

        await _rabbitMqPublisher.PublishProcessVideoCommand(new ProcessVideoCommand(
            VideoId: media.Id,
            OriginalFileUrl: dto.OriginalFileUrl,
            CourseId: Guid.Empty,
            TargetType: MediaTargetType,
            GenerateThumbnail: string.IsNullOrWhiteSpace(dto.ThumbnailUrl)));

        await _notificationService.NotifyShopFollowersAsync(
            shop.Id,
            $"{shop.ShopName} yeni bir video paylaştı!",
            "Takip ettiğiniz mağaza yeni bir video paylaştı.",
            NotificationType.NewVideo,
            media.Id);

        media.Shop = shop;
        media.Product = product;

        return MapToResponse(media);
    }

    public async Task<MediaLikeResponseDto> ToggleLikeAsync(Guid mediaId, Guid userId)
    {
        var media = await GetActiveMediaAsync(mediaId);
        var like = await _dbContext.MediaLikes.FirstOrDefaultAsync(item =>
            item.MediaId == mediaId &&
            item.UserId == userId);
        var isNewLike = like is null;

        if (isNewLike)
        {
            _dbContext.MediaLikes.Add(new MediaLike
            {
                MediaId = mediaId,
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            });
        }
        else if (like is not null)
        {
            _dbContext.MediaLikes.Remove(like);
        }

        await _dbContext.SaveChangesAsync();
        await InvalidateFeedCacheAsync();

        if (isNewLike && media.Shop.UserId != userId)
        {
            var actor = await GetNotificationActorAsync(userId);

            await _notificationService.SendActorNotificationAsync(
                media.Shop.UserId,
                "Videonuz yeni bir beğeni aldı!",
                "Paylaştığınız video yeni bir beğeni aldı.",
                NotificationType.NewLike,
                media.Id,
                actor.UserId,
                actor.FullName,
                actor.AvatarObjectKey,
                actor.ShopId,
                actor.ShopName,
                actor.ShopLogoObjectKey);
        }

        return new MediaLikeResponseDto(
            IsLiked: isNewLike,
            LikeCount: await GetMediaLikeCountAsync(mediaId));
    }

    public async Task<MediaSaveResponseDto> ToggleSaveAsync(Guid mediaId, Guid userId)
    {
        var media = await GetActiveMediaAsync(mediaId);
        var save = await _dbContext.MediaSaves.FirstOrDefaultAsync(item =>
            item.MediaId == mediaId &&
            item.UserId == userId);
        var isSaved = save is null;

        if (isSaved)
        {
            _dbContext.MediaSaves.Add(new MediaSafe
            {
                MediaId = mediaId,
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            });
        }
        else if (save is not null)
        {
            _dbContext.MediaSaves.Remove(save);
        }

        await _dbContext.SaveChangesAsync();
        await InvalidateFeedCacheAsync();

        return new MediaSaveResponseDto(
            IsSaved: isSaved,
            SaveCount: await GetMediaSaveCountAsync(mediaId));
    }

    public async Task<MediaResponseDto> RecordShareAsync(Guid mediaId, Guid userId)
    {
        _ = await GetActiveMediaAsync(mediaId);
        var shareCount = await IncrementMediaShareCountAsync(mediaId);
        if (shareCount < 0)
        {
            throw new NotFoundException("Medya bulunamadi.");
        }

        await InvalidateFeedCacheAsync();

        var media = await _dbContext.Media
            .AsNoTracking()
            .Include(item => item.Shop)
            .Include(item => item.Product)
            .FirstAsync(item => item.Id == mediaId);
        var response = MapToResponse(media);

        return (await ApplyUserMediaStateAsync([response], userId))[0];
    }

    public async Task<MediaCommentCreateResponseDto> AddCommentAsync(
        Guid mediaId,
        Guid userId,
        string text,
        Guid? parentCommentId)
    {
        var normalizedText = PlainTextInputValidator.Require(text, "Yorum metni", 1000);

        var media = await GetActiveMediaAsync(mediaId);
        var user = await _dbContext.Users
            .Include(item => item.Shop)
            .FirstOrDefaultAsync(user => user.Id == userId);

        if (user is null)
        {
            throw new UnauthorizedException("Geçersiz kullanıcı.");
        }

        if (parentCommentId.HasValue)
        {
            var parentComment = await _dbContext.MediaComments
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == parentCommentId.Value);

            if (parentComment is null)
            {
                throw new NotFoundException("Ust yorum bulunamadi.");
            }

            if (parentComment.MediaId != mediaId)
            {
                throw new BadRequestException("Baska bir videonun yorumuna cevap veremezsiniz.");
            }

            if (parentComment.ParentCommentId.HasValue)
            {
                throw new BadRequestException("Yorum cevaplarina tekrar cevap verilemez.");
            }
        }

        var comment = new MediaComment
        {
            MediaId = mediaId,
            UserId = userId,
            ParentCommentId = parentCommentId,
            CommentText = normalizedText,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.MediaComments.Add(comment);

        await _dbContext.SaveChangesAsync();
        await InvalidateFeedCacheAsync();

        if (media.Shop.UserId != userId)
        {
            var actor = CreateNotificationActor(user);

            await _notificationService.SendActorNotificationAsync(
                media.Shop.UserId,
                "Videonuz yeni bir yorum aldı!",
                "Paylaştığınız videoya yeni bir yorum geldi.",
                NotificationType.NewComment,
                media.Id,
                actor.UserId,
                actor.FullName,
                actor.AvatarObjectKey,
                actor.ShopId,
                actor.ShopName,
                actor.ShopLogoObjectKey);
        }

        return new MediaCommentCreateResponseDto(
            Comment: MapToComment(comment, user, media.ShopId),
            CommentCount: await GetMediaCommentCountAsync(mediaId));
    }

    public async Task<MediaCommentListResponseDto> GetCommentsAsync(
        Guid mediaId,
        int page = 1,
        int pageSize = 20)
    {
        var media = await GetActiveMediaAsync(mediaId);

        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 50);
        var commentsQuery = _dbContext.MediaComments
            .AsNoTracking()
            .Where(comment => comment.MediaId == mediaId && comment.ParentCommentId == null);

        var totalCount = await commentsQuery.CountAsync();
        var parents = await commentsQuery
            .Include(comment => comment.User)
                .ThenInclude(user => user.Shop)
            .Include(comment => comment.Replies)
                .ThenInclude(reply => reply.User)
                    .ThenInclude(user => user.Shop)
            .OrderByDescending(comment => comment.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .AsSplitQuery()
            .ToListAsync();

        var items = parents
            .Select(parent =>
            {
                var replies = parent.Replies
                    .OrderBy(reply => reply.CreatedAt)
                    .Select(reply => MapToComment(reply, reply.User, media.ShopId))
                    .ToList();

                return MapToComment(parent, parent.User, media.ShopId, replies);
            })
            .ToList();
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);

        return new MediaCommentListResponseDto(
            Items: items,
            Page: normalizedPage,
            PageSize: normalizedPageSize,
            TotalCount: totalCount,
            TotalPages: totalPages);
    }

    public async Task<MediaCommentDeleteResponseDto> DeleteCommentAsync(Guid commentId, Guid userId)
    {
        var comment = await _dbContext.MediaComments
            .Include(item => item.Media)
            .ThenInclude(media => media.Shop)
            .FirstOrDefaultAsync(item => item.Id == commentId);

        if (comment is null)
        {
            throw new NotFoundException("Yorum bulunamadı.");
        }

        var isCommentOwner = comment.UserId == userId;
        var isMediaOwner = comment.Media.Shop.UserId == userId;

        if (!isCommentOwner && !isMediaOwner)
        {
            throw new ForbiddenException("Bu yorumu silme yetkiniz yok.");
        }

        var mediaId = comment.MediaId;

        _dbContext.MediaComments.Remove(comment);
        await _dbContext.SaveChangesAsync();
        await InvalidateFeedCacheAsync();

        return new MediaCommentDeleteResponseDto(
            CommentCount: await GetMediaCommentCountAsync(mediaId));
    }

    public async Task DeleteMediaAsync(Guid mediaId, Guid userId)
    {
        var media = await _dbContext.Media
            .Include(item => item.Shop)
            .FirstOrDefaultAsync(item =>
                item.Id == mediaId &&
                item.IsActive == true);

        if (media is null)
        {
            throw new NotFoundException("Medya bulunamadı.");
        }

        if (media.Shop.UserId != userId)
        {
            throw new ForbiddenException("Bu medyayı silme yetkiniz yok.");
        }

        media.IsActive = false;
        media.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        await InvalidateFeedCacheAsync();
        await _rabbitMqPublisher.PublishMediaSyncMessage(new MediaSyncMessage(
            MediaId: media.Id,
            Action: "Delete",
            Document: null));

        var videoObjectKey = ExtractObjectKey(media.VideoUrl, PrivateProductsBucketName);
        if (!string.IsNullOrWhiteSpace(videoObjectKey))
        {
            await _storageService.DeleteFileAsync(PrivateProductsBucketName, videoObjectKey);
        }

        var thumbnailObjectKey = ExtractObjectKey(media.ThumbnailUrl, PublicAssetsBucketName);
        if (!string.IsNullOrWhiteSpace(thumbnailObjectKey))
        {
            await _storageService.DeleteFileAsync(PublicAssetsBucketName, thumbnailObjectKey);
        }

        var mediaIdValue = mediaId.ToString("D");
        await _cacheService.RemoveAsync(GetViewCountCacheKey(mediaId));
        await _cacheService.RemoveFromSetAsync(TrackedViewsSetKey, mediaIdValue);
    }

    public async Task RecordViewAsync(
        Guid mediaId,
        Guid? userId,
        System.Net.IPAddress? ipAddress,
        string? userAgent,
        string? referrer)
    {
        var mediaExists = await _dbContext.Media.AnyAsync(item =>
            item.Id == mediaId &&
            item.IsActive == true);

        if (!mediaExists)
        {
            throw new NotFoundException("Medya bulunamadı.");
        }

        await _cacheService.IncrementAsync(
            GetViewCountCacheKey(mediaId),
            absoluteExpirationRelativeToNow: ViewCountCacheTtl);

        await _cacheService.AddToSetAsync(TrackedViewsSetKey, mediaId.ToString("D"));

        if (userId.HasValue)
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO media_watch_history (media_id, user_id, watched_at, is_point_earned)
                VALUES ({mediaId}, {userId.Value}, {DateTime.UtcNow}, {false})
                ON CONFLICT (user_id, media_id) DO NOTHING
                """);
        }

        await _analyticsEventService.TrackMediaViewAsync(mediaId, userId, ipAddress, userAgent, referrer);
    }

    private async Task<Medium> GetActiveMediaAsync(Guid mediaId)
    {
        var media = await _dbContext.Media
            .Include(item => item.Shop)
            .FirstOrDefaultAsync(item =>
                item.Id == mediaId &&
                item.IsActive == true);

        if (media is null)
        {
            throw new NotFoundException("Medya bulunamadı.");
        }

        return media;
    }

    private MediaResponseDto MapToResponse(Medium media)
    {
        var availableProduct = media.Product is
            { IsActive: true, Status: ProductStatus.Published }
            ? media.Product
            : null;

        return new MediaResponseDto(
            Id: media.Id,
            ShopId: media.ShopId,
            ShopName: media.Shop.ShopName,
            ShopSlug: media.Shop.Slug,
            ShopLogoUrl: media.Shop.LogoUrl,
            ShopLogoPublicUrl: GenerateStoragePublicUrl(media.Shop.LogoUrl, PublicAssetsBucketName),
            IsShopVerified: media.Shop.IsVerified == true,
            ProductId: availableProduct?.Id,
            ProductTitle: availableProduct?.Title,
            ProductType: ToProductTypeName(availableProduct?.Type),
            ProductCoverImagePublicUrl: GenerateStoragePublicUrl(
                availableProduct?.CoverImageUrl,
                PublicAssetsBucketName),
            VideoUrl: media.VideoUrl,
            VideoPublicUrl: GenerateStoragePublicUrl(media.VideoUrl, PrivateProductsBucketName),
            ThumbnailUrl: media.ThumbnailUrl,
            ThumbnailPublicUrl: GenerateStoragePublicUrl(media.ThumbnailUrl, PublicAssetsBucketName),
            Caption: media.Caption,
            Hashtags: media.Hashtags ?? new List<string>(),
            ViewCount: media.ViewCount ?? 0,
            LikeCount: media.LikeCount ?? 0,
            CommentCount: media.CommentCount ?? 0,
            SaveCount: media.SaveCount ?? 0,
            ShareCount: media.ShareCount ?? 0,
            Status: media.Status.ToString(),
            CreatedAt: media.CreatedAt);
    }

    private async Task<List<MediaResponseDto>> ApplyUserMediaStateAsync(
        List<MediaResponseDto> feed,
        Guid? currentUserId)
    {
        if (!currentUserId.HasValue || feed.Count == 0)
        {
            return feed;
        }

        var mediaIds = feed.Select(item => item.Id).ToList();

        var likedMediaIds = await _dbContext.MediaLikes
            .AsNoTracking()
            .Where(item =>
                item.UserId == currentUserId.Value &&
                mediaIds.Contains(item.MediaId))
            .Select(item => item.MediaId)
            .ToListAsync();

        var savedMediaIds = await _dbContext.MediaSaves
            .AsNoTracking()
            .Where(item =>
                item.UserId == currentUserId.Value &&
                mediaIds.Contains(item.MediaId))
            .Select(item => item.MediaId)
            .ToListAsync();

        var likedSet = likedMediaIds.ToHashSet();
        var savedSet = savedMediaIds.ToHashSet();

        return feed
            .Select(item => item with
            {
                IsLiked = likedSet.Contains(item.Id),
                IsSaved = savedSet.Contains(item.Id)
            })
            .ToList();
    }

    private List<MediaResponseDto> ApplyDiscoveryTrackingTokens(
        List<MediaResponseDto> feed,
        Guid? currentUserId,
        int page,
        int pageSize)
    {
        if (feed.Count == 0)
        {
            return feed;
        }

        var feedSessionId = Guid.NewGuid();
        var startPosition = (page - 1) * pageSize;

        return feed
            .Select((item, index) => item with
            {
                TrackingToken = _discoveryTrackingTokenService.Issue(
                    currentUserId,
                    "media",
                    item.Id,
                    item.ShopId,
                    feedSessionId,
                    startPosition + index)
            })
            .ToList();
    }

    private async Task<List<MediaResponseDto>> FilterToActiveShopMediaAsync(
        List<MediaResponseDto> media)
    {
        if (media.Count == 0)
        {
            return media;
        }

        var shopIds = media
            .Select(item => item.ShopId)
            .Distinct()
            .ToList();
        var activeShopIds = await _dbContext.Shops
            .AsNoTracking()
            .Where(shop =>
                shop.IsActive == true &&
                shopIds.Contains(shop.Id))
            .Select(shop => shop.Id)
            .ToListAsync();
        var activeShopIdSet = activeShopIds.ToHashSet();

        return media
            .Where(item => activeShopIdSet.Contains(item.ShopId))
            .ToList();
    }

    private async Task<string> GetFeedCacheKeyAsync(int page, int pageSize)
    {
        var version = await _cacheService.GetAsync<long>(FeedCacheVersionKey);
        return $"media:feed:v:{version}:page:{page}:size:{pageSize}";
    }

    private async Task<string> GetLikedMediaCacheKeyAsync(Guid userId, int page, int pageSize)
    {
        var version = await _cacheService.GetAsync<long>(LikedMediaCacheVersionKey);
        return $"media:liked:v:{version}:user:{userId}:page:{page}:size:{pageSize}";
    }

    private async Task InvalidateFeedCacheAsync()
    {
        await _cacheService.IncrementAsync(FeedCacheVersionKey);
        await _cacheService.IncrementAsync(LikedMediaCacheVersionKey);
    }

    private async Task<int> GetMediaLikeCountAsync(Guid mediaId)
    {
        return await _dbContext.Media
            .AsNoTracking()
            .Where(item => item.Id == mediaId)
            .Select(item => item.LikeCount ?? 0)
            .FirstAsync();
    }

    private async Task<int> GetMediaSaveCountAsync(Guid mediaId)
    {
        return await _dbContext.Media
            .AsNoTracking()
            .Where(item => item.Id == mediaId)
            .Select(item => item.SaveCount ?? 0)
            .FirstAsync();
    }

    private async Task<int> GetMediaCommentCountAsync(Guid mediaId)
    {
        return await _dbContext.Media
            .AsNoTracking()
            .Where(item => item.Id == mediaId)
            .Select(item => item.CommentCount ?? 0)
            .FirstAsync();
    }

    private async Task<int> IncrementMediaShareCountAsync(Guid mediaId)
    {
        var connection = _dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await _dbContext.Database.OpenConnectionAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT public.increment_media_share_count(CAST(@media_id AS uuid))
                """;
            AddParameter(command, "media_id", mediaId);

            var result = await command.ExecuteScalarAsync();
            return result is null or DBNull
                ? -1
                : Convert.ToInt32(result);
        }
        finally
        {
            if (openedHere)
            {
                await _dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private async Task PublishMediaIndexMessageAsync(Guid mediaId)
    {
        var media = await _dbContext.Media
            .AsNoTracking()
            .Include(item => item.Shop)
            .Include(item => item.Product)
            .FirstOrDefaultAsync(item => item.Id == mediaId);

        if (media is null || media.IsActive != true || media.Shop.IsActive != true)
        {
            await _rabbitMqPublisher.PublishMediaSyncMessage(new MediaSyncMessage(
                MediaId: mediaId,
                Action: "Delete",
                Document: null));
            return;
        }

        var document = new MediaDocument
        {
            Id = media.Id,
            Caption = media.Caption,
            Hashtags = media.Hashtags ?? new List<string>(),
            ShopId = media.ShopId,
            ShopName = media.Shop.ShopName,
            ShopSlug = media.Shop.Slug,
            ProductId = media.ProductId,
            ProductTitle = media.Product?.Title,
            ProductType = ToProductTypeName(media.Product?.Type),
            ThumbnailObjectKey = ExtractObjectKey(media.ThumbnailUrl, PublicAssetsBucketName),
            VideoObjectKey = ExtractObjectKey(media.VideoUrl, PrivateProductsBucketName),
            ProductCoverImageObjectKey = ExtractObjectKey(media.Product?.CoverImageUrl, PublicAssetsBucketName),
            IsActive = true,
            ShopIsActive = true,
            CreatedAt = media.CreatedAt,
            ViewCount = media.ViewCount ?? 0,
            LikeCount = media.LikeCount ?? 0,
            SaveCount = media.SaveCount ?? 0,
            ShareCount = media.ShareCount ?? 0
        };

        await _rabbitMqPublisher.PublishMediaSyncMessage(new MediaSyncMessage(
            MediaId: media.Id,
            Action: "Index",
            Document: document));
    }

    private string? GenerateStoragePublicUrl(string? urlOrObjectKey, string bucketName)
    {
        var objectKey = ExtractObjectKey(urlOrObjectKey, bucketName);
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return null;
        }

        return _storageService.GeneratePresignedDownloadUrl(
            bucketName,
            objectKey,
            PublicUrlExpiryMinutes);
    }

    private static string? ToProductTypeName(ProductType? productType)
    {
        return productType switch
        {
            ProductType.Course => "course",
            ProductType.DigitalFile => "digital_file",
            _ => null
        };
    }

    private static List<string> NormalizeHashtags(IEnumerable<string>? hashtags)
    {
        var normalized = hashtags?
            .Where(hashtag => !string.IsNullOrWhiteSpace(hashtag))
            .Select(hashtag => hashtag.Trim().TrimStart('#').ToLowerInvariant())
            .Where(hashtag => hashtag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        if (normalized.Count > 20 || normalized.Any(hashtag => hashtag.Length > 50))
        {
            throw new BadRequestException(
                "En fazla 20 adet ve 50 karakterlik hashtag kullanilabilir.");
        }

        return normalized;
    }

    private static string GetViewCountCacheKey(Guid mediaId)
    {
        return $"media:viewcount:{mediaId}";
    }

    private static string? ExtractObjectKey(string? urlOrObjectKey, string bucketName)
    {
        if (string.IsNullOrWhiteSpace(urlOrObjectKey))
        {
            return null;
        }

        if (!Uri.TryCreate(urlOrObjectKey, UriKind.Absolute, out var uri))
        {
            return urlOrObjectKey.TrimStart('/');
        }

        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        var bucketPrefix = $"{bucketName}/";
        var bucketIndex = path.IndexOf(bucketPrefix, StringComparison.OrdinalIgnoreCase);

        return bucketIndex >= 0
            ? path[(bucketIndex + bucketPrefix.Length)..]
            : path;
    }

    private CommentDto MapToComment(
        MediaComment comment,
        User? user,
        Guid mediaShopId,
        IReadOnlyList<CommentDto>? replies = null)
    {
        var shop = user?.Shop;

        return new CommentDto
        {
            Id = comment.Id,
            UserId = comment.UserId,
            ParentCommentId = comment.ParentCommentId,
            UserName = user?.FullName,
            UserEmail = user?.Email,
            AvatarPublicUrl = GenerateStoragePublicUrl(user?.AvatarUrl, PublicAssetsBucketName),
            ShopId = shop?.Id,
            ShopName = shop?.ShopName,
            ShopLogoPublicUrl = GenerateStoragePublicUrl(shop?.LogoUrl, PublicAssetsBucketName),
            IsShopAuthor = shop?.Id == mediaShopId,
            Text = comment.CommentText,
            CreatedAt = comment.CreatedAt,
            ReplyCount = replies?.Count ?? 0,
            Replies = replies ?? Array.Empty<CommentDto>()
        };
    }

    private async Task<MediaNotificationActor> GetNotificationActorAsync(Guid userId)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(item => item.Shop)
            .FirstOrDefaultAsync(item => item.Id == userId);

        if (user is null)
        {
            throw new UnauthorizedException("Gecersiz kullanici.");
        }

        return CreateNotificationActor(user);
    }

    private static MediaNotificationActor CreateNotificationActor(User user)
    {
        var shop = user.Shop?.IsActive == true
            ? user.Shop
            : null;

        return new MediaNotificationActor(
            UserId: user.Id,
            FullName: user.FullName,
            AvatarObjectKey: user.AvatarUrl,
            ShopId: shop?.Id,
            ShopName: shop?.ShopName,
            ShopLogoObjectKey: shop?.LogoUrl);
    }

    private sealed record MediaNotificationActor(
        Guid UserId,
        string? FullName,
        string? AvatarObjectKey,
        Guid? ShopId,
        string? ShopName,
        string? ShopLogoObjectKey);
}
