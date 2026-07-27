namespace CraftoraApi.Infrastructure.Messaging.Contracts;

public sealed record SendPushNotificationCommand(
    Guid NotificationId,
    Guid UserId,
    string Title,
    string Body,
    Dictionary<string, string> Data);
