using CraftoraApi.Data;
using CraftoraApi.DTOs.Notification;
using CraftoraApi.Hubs;
using CraftoraApi.Infrastructure.Messaging;
using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.Infrastructure.Security;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CraftoraApi.Services;

public sealed class NotificationService : INotificationService
{
    private const string PublicAssetsBucketName = "public-assets";
    private const int PublicUrlExpiryMinutes = 60;

    private readonly AppDbContext _dbContext;
    private readonly IRabbitMqPublisher _rabbitMqPublisher;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly IStorageService _storageService;

    public NotificationService(
        AppDbContext dbContext,
        IRabbitMqPublisher rabbitMqPublisher,
        IHubContext<NotificationHub> hubContext,
        IStorageService storageService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _rabbitMqPublisher = rabbitMqPublisher ?? throw new ArgumentNullException(nameof(rabbitMqPublisher));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
    }

    public async Task<List<NotificationDto>> GetUserNotificationsAsync(
        Guid userId,
        int page = 1,
        int pageSize = 20)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 50);

        var notifications = await _dbContext.Notifications
            .AsNoTracking()
            .Where(notification => notification.UserId == userId)
            .OrderByDescending(notification => notification.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();

        return notifications.Select(MapToDto).ToList();
    }

    public async Task MarkAsReadAsync(Guid notificationId, Guid userId)
    {
        var notification = await _dbContext.Notifications.FirstOrDefaultAsync(item =>
            item.Id == notificationId &&
            item.UserId == userId);

        if (notification is null)
        {
            throw new NotFoundException("Bildirim bulunamadı.");
        }

        if (notification.IsRead == true)
        {
            return;
        }

        notification.IsRead = true;
        notification.ReadAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
    }

    public async Task MarkAllAsReadAsync(Guid userId)
    {
        var unreadNotifications = await _dbContext.Notifications
            .Where(notification =>
                notification.UserId == userId &&
                notification.IsRead != true)
            .ToListAsync();

        foreach (var notification in unreadNotifications)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task SaveDeviceTokenAsync(Guid userId, SaveDeviceTokenDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (string.IsNullOrWhiteSpace(dto.DeviceToken))
        {
            throw new BadRequestException("Cihaz token bilgisi zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(dto.DeviceType))
        {
            throw new BadRequestException("Cihaz tipi zorunludur.");
        }

        var userExists = await _dbContext.Users.AnyAsync(user => user.Id == userId);
        if (!userExists)
        {
            throw new UnauthorizedException("Geçersiz kullanıcı.");
        }

        var normalizedDeviceType = dto.DeviceType.Trim().ToLowerInvariant();
        var deviceToken = dto.DeviceToken.Trim();

        var existingToken = await _dbContext.UserDeviceTokens.FirstOrDefaultAsync(token =>
            token.UserId == userId &&
            token.Token == deviceToken);

        if (existingToken is null)
        {
            _dbContext.UserDeviceTokens.Add(new UserDeviceToken
            {
                UserId = userId,
                Token = deviceToken,
                DeviceType = normalizedDeviceType,
                IsActive = true,
                LastUsedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existingToken.DeviceType = normalizedDeviceType;
            existingToken.IsActive = true;
            existingToken.LastUsedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task SendNotificationAsync(
        Guid userId,
        string title,
        string message,
        NotificationType type,
        Guid? referenceId,
        string? referenceType = null)
    {
        var normalizedTitle = PlainTextInputValidator.Require(title, "Bildirim basligi", 200);
        var normalizedMessage = PlainTextInputValidator.Require(message, "Bildirim mesaji", 1000);

        var userExists = await _dbContext.Users.AnyAsync(user => user.Id == userId);
        if (!userExists)
        {
            throw new NotFoundException("Bildirim gönderilecek kullanıcı bulunamadı.");
        }

        var notification = new Notification
        {
            UserId = userId,
            Title = normalizedTitle,
            Body = normalizedMessage,
            Type = ToStorageValue(type),
            ReferenceType = PlainTextInputValidator.Optional(referenceType, "Bildirim referans tipi", 50)
                ?? ToReferenceType(type),
            ReferenceId = referenceId,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync();

        var notificationDto = MapToDto(notification);
        await PublishRealtimeNotificationAsync(userId, notificationDto);

        await _rabbitMqPublisher.PublishPushNotificationCommand(new SendPushNotificationCommand(
            UserId: userId,
            Title: notification.Title,
            Body: notification.Body,
            Data: new Dictionary<string, string>
            {
                ["notificationId"] = notification.Id.ToString("D"),
                ["type"] = type.ToString(),
                ["referenceType"] = notification.ReferenceType ?? string.Empty,
                ["referenceId"] = referenceId?.ToString("D") ?? string.Empty
            }));
    }

    public async Task SendProductQuestionAnswerNotificationAsync(
        Guid userId,
        Guid productId,
        Guid questionId,
        Guid shopId,
        string shopName,
        string? shopLogoObjectKey,
        string answerText)
    {
        var normalizedShopName = PlainTextInputValidator.Require(shopName, "Magaza adi", 200);
        var normalizedAnswer = PlainTextInputValidator.Require(answerText, "Cevap metni", 1000);
        var title = $"{normalizedShopName} sorunu yanitladi";

        var notification = new Notification
        {
            UserId = userId,
            Title = title,
            Body = normalizedAnswer,
            Type = ToStorageValue(NotificationType.ProductQuestionAnswer),
            ReferenceType = ToReferenceType(NotificationType.ProductQuestionAnswer),
            ReferenceId = questionId,
            Data = JsonSerializer.Serialize(new ProductQuestionAnswerNotificationData(
                ProductId: productId,
                QuestionId: questionId,
                Actor: new NotificationActorData(
                    UserId: null,
                    FullName: null,
                    AvatarObjectKey: null,
                    ShopId: shopId,
                    ShopName: normalizedShopName,
                    ShopLogoObjectKey: NormalizeObjectKey(shopLogoObjectKey)))),
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync();

        var notificationDto = MapToDto(notification);
        await PublishRealtimeNotificationAsync(userId, notificationDto);

        await _rabbitMqPublisher.PublishPushNotificationCommand(new SendPushNotificationCommand(
            UserId: userId,
            Title: notification.Title,
            Body: notification.Body,
            Data: new Dictionary<string, string>
            {
                ["notificationId"] = notification.Id.ToString("D"),
                ["type"] = ToStorageValue(NotificationType.ProductQuestionAnswer),
                ["referenceType"] = notification.ReferenceType ?? string.Empty,
                ["referenceId"] = questionId.ToString("D"),
                ["productId"] = productId.ToString("D"),
                ["questionId"] = questionId.ToString("D"),
                ["shopId"] = shopId.ToString("D")
            }));
    }

    public async Task SendActorNotificationAsync(
        Guid userId,
        string title,
        string message,
        NotificationType type,
        Guid referenceId,
        Guid actorUserId,
        string? actorFullName,
        string? actorAvatarObjectKey,
        Guid? actorShopId,
        string? actorShopName,
        string? actorShopLogoObjectKey,
        string? referenceType = null)
    {
        var normalizedTitle = PlainTextInputValidator.Require(title, "Bildirim basligi", 200);
        var normalizedMessage = PlainTextInputValidator.Require(message, "Bildirim mesaji", 1000);
        var normalizedReferenceType = PlainTextInputValidator.Optional(referenceType, "Bildirim referans tipi", 50)
            ?? ToReferenceType(type);

        var notification = new Notification
        {
            UserId = userId,
            Title = normalizedTitle,
            Body = normalizedMessage,
            Type = ToStorageValue(type),
            ReferenceType = normalizedReferenceType,
            ReferenceId = referenceId,
            Data = JsonSerializer.Serialize(new ActorNotificationData(
                Actor: new NotificationActorData(
                    UserId: actorUserId,
                    FullName: actorFullName,
                    AvatarObjectKey: NormalizeObjectKey(actorAvatarObjectKey),
                    ShopId: actorShopId,
                    ShopName: actorShopName,
                    ShopLogoObjectKey: NormalizeObjectKey(actorShopLogoObjectKey)))),
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync();

        var notificationDto = MapToDto(notification);
        await PublishRealtimeNotificationAsync(userId, notificationDto);

        await _rabbitMqPublisher.PublishPushNotificationCommand(new SendPushNotificationCommand(
            UserId: userId,
            Title: notification.Title,
            Body: notification.Body,
            Data: new Dictionary<string, string>
            {
                ["notificationId"] = notification.Id.ToString("D"),
                ["type"] = ToStorageValue(type),
                ["referenceType"] = notification.ReferenceType ?? string.Empty,
                ["referenceId"] = referenceId.ToString("D"),
                ["actorUserId"] = actorUserId.ToString("D"),
                ["actorShopId"] = actorShopId?.ToString("D") ?? string.Empty
            }));
    }

    public async Task NotifyShopFollowersAsync(
        Guid shopId,
        string title,
        string message,
        NotificationType type,
        Guid? referenceId)
    {
        var shop = await _dbContext.Shops
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == shopId && item.IsActive == true);

        if (shop is null)
        {
            throw new NotFoundException("Mağaza bulunamadı.");
        }

        var followerIds = await _dbContext.Subscriptions
            .AsNoTracking()
            .Where(subscription =>
                subscription.ShopId == shopId &&
                subscription.UserId != shop.UserId &&
                subscription.WantsNotifications == true)
            .Select(subscription => subscription.UserId)
            .Distinct()
            .ToListAsync();

        foreach (var followerId in followerIds)
        {
            await SendNotificationAsync(followerId, title, message, type, referenceId);
        }
    }

    private NotificationDto MapToDto(Notification notification)
    {
        var productQuestionAnswerData = TryReadProductQuestionAnswerData(notification.Data);
        var actorData = TryReadActorNotificationData(notification.Data);

        return new NotificationDto(
            Id: notification.Id,
            UserId: notification.UserId,
            Title: notification.Title,
            Message: notification.Body,
            Type: FromStorageValue(notification.Type),
            IsRead: notification.IsRead == true,
            ReferenceType: notification.ReferenceType,
            ReferenceId: notification.ReferenceId,
            CreatedAt: notification.CreatedAt,
            ProductId: productQuestionAnswerData?.ProductId,
            QuestionId: productQuestionAnswerData?.QuestionId,
            Actor: MapActor(actorData?.Actor ?? productQuestionAnswerData?.Actor));
    }

    private static string ToStorageValue(NotificationType type)
    {
        return type switch
        {
            NotificationType.NewVideo => "new_video",
            NotificationType.NewProduct => "new_product",
            NotificationType.NewLike => "media_liked",
            NotificationType.NewComment => "media_commented",
            NotificationType.NewOrder => "order_completed",
            NotificationType.ProductQuestionAnswer => "product_question_answer",
            NotificationType.System => "system",
            _ => "system"
        };
    }

    private static NotificationType FromStorageValue(string value)
    {
        return value switch
        {
            "new_video" => NotificationType.NewVideo,
            "new_product" => NotificationType.NewProduct,
            "media_liked" => NotificationType.NewLike,
            "media_commented" => NotificationType.NewComment,
            "order_completed" => NotificationType.NewOrder,
            "product_question_answer" => NotificationType.ProductQuestionAnswer,
            _ => NotificationType.System
        };
    }

    private static string ToReferenceType(NotificationType type)
    {
        return type switch
        {
            NotificationType.NewVideo => "media",
            NotificationType.NewProduct => "product",
            NotificationType.NewLike => "media",
            NotificationType.NewComment => "media",
            NotificationType.NewOrder => "order",
            NotificationType.ProductQuestionAnswer => "product_question_answer",
            NotificationType.System => "system",
            _ => "system"
        };
    }

    private async Task PublishRealtimeNotificationAsync(Guid userId, NotificationDto notification)
    {
        await _hubContext
            .Clients
            .Group(NotificationHub.UserGroup(userId))
            .SendAsync("ReceiveNotification", notification);
    }

    private NotificationActorDto? MapActor(NotificationActorData? actor)
    {
        if (actor is null)
        {
            return null;
        }

        return new NotificationActorDto(
            UserId: actor.UserId,
            FullName: actor.FullName,
            AvatarPublicUrl: GeneratePublicAssetUrl(actor.AvatarObjectKey),
            ShopId: actor.ShopId,
            ShopName: actor.ShopName,
            ShopLogoPublicUrl: GeneratePublicAssetUrl(actor.ShopLogoObjectKey));
    }

    private string? GeneratePublicAssetUrl(string? objectKey)
    {
        var normalizedObjectKey = NormalizeObjectKey(objectKey);
        return string.IsNullOrWhiteSpace(normalizedObjectKey)
            ? null
            : _storageService.GeneratePresignedDownloadUrl(
                PublicAssetsBucketName,
                normalizedObjectKey,
                PublicUrlExpiryMinutes);
    }

    private static string? NormalizeObjectKey(string? objectKey)
    {
        return string.IsNullOrWhiteSpace(objectKey)
            ? null
            : objectKey.Trim().TrimStart('/');
    }

    private static ProductQuestionAnswerNotificationData? TryReadProductQuestionAnswerData(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProductQuestionAnswerNotificationData>(
                data,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ActorNotificationData? TryReadActorNotificationData(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ActorNotificationData>(
                data,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ProductQuestionAnswerNotificationData(
        Guid? ProductId,
        Guid? QuestionId,
        NotificationActorData Actor);

    private sealed record ActorNotificationData(
        NotificationActorData Actor);

    private sealed record NotificationActorData(
        Guid? UserId,
        string? FullName,
        string? AvatarObjectKey,
        Guid? ShopId,
        string? ShopName,
        string? ShopLogoObjectKey);
}
