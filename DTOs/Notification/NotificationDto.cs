namespace CraftoraApi.DTOs.Notification;

public sealed record NotificationActorDto(
    Guid? UserId,
    string? FullName,
    string? AvatarPublicUrl,
    Guid? ShopId,
    string? ShopName,
    string? ShopLogoPublicUrl);

public sealed record NotificationDto(
    Guid Id,
    Guid UserId,
    string Title,
    string Message,
    NotificationType Type,
    bool IsRead,
    string? ReferenceType,
    Guid? ReferenceId,
    DateTime? CreatedAt,
    Guid? ProductId = null,
    Guid? QuestionId = null,
    NotificationActorDto? Actor = null);
