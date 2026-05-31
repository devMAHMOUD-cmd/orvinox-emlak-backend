using CraftoraApi.DTOs.Notification;

namespace CraftoraApi.Services.Interfaces;

public interface INotificationService
{
    Task<List<NotificationDto>> GetUserNotificationsAsync(Guid userId, int page = 1, int pageSize = 20);

    Task MarkAsReadAsync(Guid notificationId, Guid userId);

    Task MarkAllAsReadAsync(Guid userId);

    Task SaveDeviceTokenAsync(Guid userId, SaveDeviceTokenDto dto);

    Task SendNotificationAsync(
        Guid userId,
        string title,
        string message,
        NotificationType type,
        Guid? referenceId);

    Task NotifyShopFollowersAsync(
        Guid shopId,
        string title,
        string message,
        NotificationType type,
        Guid? referenceId);
}
