namespace CraftoraApi.DTOs.Notification;

public sealed record NotificationDto(
    Guid Id,
    Guid UserId,
    string Title,
    string Message,
    NotificationType Type,
    bool IsRead,
    Guid? ReferenceId,
    DateTime? CreatedAt);
