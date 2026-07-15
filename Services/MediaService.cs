using CraftoraApi.Data;
using CraftoraApi.DTOs.Media;
using CraftoraApi.DTOs.Notification;
using CraftoraApi.Infrastructure.Messaging;
using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.Infrastructure.Security;
using CraftoraApi.Middleware;
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
    private const string FeedCacheVersionKey = "media:feed:version";
    private static readonly TimeSpan FeedCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ViewCountCacheTtl = TimeSpan.FromHours(24);

    private readonly AppDbContext _dbContext;
    private readonly IRabbitMqPublisher _rabbitMqPublisher;
    private readonly ICacheService _cacheService;
    private readonly IStorageService _storageService;
    private readonly INotificationService _notificationService;

    public MediaService(
        AppDbContext dbContext,
        IRabbitMqPublisher rabbitMqPublisher,
        ICacheService cacheService,
        IStorageService storageService,
        INotificationService notificationService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _rabbitMqPublisher = rabbitMqPublisher ?? throw new ArgumentNullException(nameof(rabbitMqPublisher));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
    }

    public async Task<List<MediaResponseDto>> GetFeedAsync(Guid? currentUserId, int page = 1, int pageSize = 10)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 50);
        var cacheKey = await GetFeedCacheKeyAsync(normalizedPage, normalizedPageSize);

        var cachedFeed = await _cacheService.GetAsync<List<MediaResponseDto>>(cacheKey);
        if (cachedFeed is not null)
        {
            var activeShopFeed = await FilterToActiveShopMediaAsync(cachedFeed);
            if (activeShopFeed.Count != cachedFeed.Count)
            {
                await _cacheService.RemoveAsync(cacheKey);
            }

            return await ApplyUserMediaStateAsync(activeShopFeed, currentUserId);
        }

        var media = await _dbContext.Media
            .AsNoTracking()
            .Include(item => item.Shop)
            .Include(item => item.Product)
            .Where(item =>
                item.IsActive == true &&
                item.Shop.IsActive == true)
            .OrderByDescending(item => item.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();

        var anonymousFeed = media.Select(MapToResponse).ToList();
        await _cacheService.SetAsync(cacheKey, anonymousFeed, FeedCacheTtl);

        return await ApplyUserMediaStateAsync(anonymousFeed, currentUserId);
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

        var productExists = await _dbContext.Products.AnyAsync(product =>
            product.Id == dto.ProductId &&
            product.ShopId == shop.Id &&
            product.IsActive == true);

        if (!productExists)
        {
            throw new NotFoundException("Ürün bulunamadı veya bu mağazaya ait değil.");
        }

        var media = new Medium
        {
            ShopId = shop.Id,
            ProductId = dto.ProductId,
            VideoUrl = dto.OriginalFileUrl,
            Caption = dto.Caption,
            ViewCount = 0,
            LikeCount = 0,
            SaveCount = 0,
            CommentCount = 0,
            Status = MediaStatus.Processing,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Media.Add(media);
        await _dbContext.SaveChangesAsync();
        await InvalidateFeedCacheAsync();

        await _rabbitMqPublisher.PublishProcessVideoCommand(new ProcessVideoCommand(
            VideoId: media.Id,
            OriginalFileUrl: dto.OriginalFileUrl,
            CourseId: Guid.Empty,
            TargetType: MediaTargetType));

        await _notificationService.NotifyShopFollowersAsync(
            shop.Id,
            $"{shop.ShopName} yeni bir video paylaştı!",
            "Takip ettiğiniz mağaza yeni bir video paylaştı.",
            NotificationType.NewVideo,
            media.Id);

        media.Shop = shop;

        return MapToResponse(media);
    }

    public async Task ToggleLikeAsync(Guid mediaId, Guid userId)
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

        media.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        if (isNewLike && media.Shop.UserId != userId)
        {
            await _notificationService.SendNotificationAsync(
                media.Shop.UserId,
                "Videonuz yeni bir beğeni aldı!",
                "Paylaştığınız video yeni bir beğeni aldı.",
                NotificationType.NewLike,
                media.Id);
        }
    }

    public async Task ToggleSaveAsync(Guid mediaId, Guid userId)
    {
        var media = await GetActiveMediaAsync(mediaId);
        var save = await _dbContext.MediaSaves.FirstOrDefaultAsync(item =>
            item.MediaId == mediaId &&
            item.UserId == userId);

        if (save is null)
        {
            _dbContext.MediaSaves.Add(new MediaSafe
            {
                MediaId = mediaId,
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            _dbContext.MediaSaves.Remove(save);
        }

        media.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }

    public async Task<CommentDto> AddCommentAsync(
        Guid mediaId,
        Guid userId,
        string text,
        Guid? parentCommentId)
    {
        var normalizedText = PlainTextInputValidator.Require(text, "Yorum metni", 1000);

        var media = await GetActiveMediaAsync(mediaId);
        var user = await _dbContext.Users
            .AsNoTracking()
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
        media.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        if (media.Shop.UserId != userId)
        {
            await _notificationService.SendNotificationAsync(
                media.Shop.UserId,
                "Videonuz yeni bir yorum aldı!",
                "Paylaştığınız videoya yeni bir yorum geldi.",
                NotificationType.NewComment,
                media.Id);
        }

        return MapToComment(comment, user.FullName);
    }

    public async Task<MediaCommentListResponseDto> GetCommentsAsync(
        Guid mediaId,
        int page = 1,
        int pageSize = 20)
    {
        _ = await GetActiveMediaAsync(mediaId);

        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 50);
        var commentsQuery = _dbContext.MediaComments
            .AsNoTracking()
            .Where(comment => comment.MediaId == mediaId && comment.ParentCommentId == null);

        var totalCount = await commentsQuery.CountAsync();
        var parents = await commentsQuery
            .Include(comment => comment.User)
            .Include(comment => comment.Replies)
                .ThenInclude(reply => reply.User)
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
                    .Select(reply => MapToComment(reply, reply.User?.FullName))
                    .ToList();

                return MapToComment(parent, parent.User?.FullName, replies);
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

    public async Task DeleteCommentAsync(Guid commentId, Guid userId)
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

        comment.Media.UpdatedAt = DateTime.UtcNow;

        _dbContext.MediaComments.Remove(comment);
        await _dbContext.SaveChangesAsync();
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
            throw new NotFoundException("Medya bulunamadÄ±.");
        }

        if (media.Shop.UserId != userId)
        {
            throw new ForbiddenException("Bu medyayÄ± silme yetkiniz yok.");
        }

        media.IsActive = false;
        media.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        await InvalidateFeedCacheAsync();

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

    public async Task RecordViewAsync(Guid mediaId, Guid? userId)
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

    private static MediaResponseDto MapToResponse(Medium media)
    {
        return new MediaResponseDto(
            Id: media.Id,
            ShopId: media.ShopId,
            ShopName: media.Shop.ShopName,
            ShopLogoUrl: media.Shop.LogoUrl,
            IsShopVerified: media.Shop.IsVerified == true,
            ProductId: media.ProductId,
            VideoUrl: media.VideoUrl,
            ThumbnailUrl: media.ThumbnailUrl,
            Caption: media.Caption,
            ViewCount: media.ViewCount ?? 0,
            LikeCount: media.LikeCount ?? 0,
            CommentCount: media.CommentCount ?? 0,
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

    private async Task InvalidateFeedCacheAsync()
    {
        await _cacheService.IncrementAsync(FeedCacheVersionKey);
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

    private static CommentDto MapToComment(
        MediaComment comment,
        string? userName,
        IReadOnlyList<CommentDto>? replies = null)
    {
        return new CommentDto
        {
            Id = comment.Id,
            UserId = comment.UserId,
            ParentCommentId = comment.ParentCommentId,
            UserName = userName,
            Text = comment.CommentText,
            CreatedAt = comment.CreatedAt,
            ReplyCount = replies?.Count ?? 0,
            Replies = replies ?? Array.Empty<CommentDto>()
        };
    }
}
