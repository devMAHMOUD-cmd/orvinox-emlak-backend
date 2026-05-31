using CraftoraApi.Data;
using CraftoraApi.DTOs.Notification;
using CraftoraApi.Infrastructure.Messaging;
using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class NotificationService : INotificationService
{
    private readonly AppDbContext _dbContext;
    private readonly IRabbitMqPublisher _rabbitMqPublisher;

    public NotificationService(
        AppDbContext dbContext,
        IRabbitMqPublisher rabbitMqPublisher)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _rabbitMqPublisher = rabbitMqPublisher ?? throw new ArgumentNullException(nameof(rabbitMqPublisher));
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
        Guid? referenceId)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new BadRequestException("Bildirim başlığı boş olamaz.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new BadRequestException("Bildirim mesajı boş olamaz.");
        }

        var userExists = await _dbContext.Users.AnyAsync(user => user.Id == userId);
        if (!userExists)
        {
            throw new NotFoundException("Bildirim gönderilecek kullanıcı bulunamadı.");
        }

        var notification = new Notification
        {
            UserId = userId,
            Title = title.Trim(),
            Body = message.Trim(),
            Type = ToStorageValue(type),
            ReferenceType = ToReferenceType(type),
            ReferenceId = referenceId,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync();

        await _rabbitMqPublisher.PublishPushNotificationCommand(new SendPushNotificationCommand(
            UserId: userId,
            Title: notification.Title,
            Body: notification.Body,
            Data: new Dictionary<string, string>
            {
                ["notificationId"] = notification.Id.ToString("D"),
                ["type"] = type.ToString(),
                ["referenceId"] = referenceId?.ToString("D") ?? string.Empty
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

    private static NotificationDto MapToDto(Notification notification)
    {
        return new NotificationDto(
            Id: notification.Id,
            UserId: notification.UserId,
            Title: notification.Title,
            Message: notification.Body,
            Type: FromStorageValue(notification.Type),
            IsRead: notification.IsRead == true,
            ReferenceId: notification.ReferenceId,
            CreatedAt: notification.CreatedAt);
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
            NotificationType.System => "system",
            _ => "system"
        };
    }
}
