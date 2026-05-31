namespace CraftoraApi.Infrastructure.Messaging.Contracts;

public sealed record SendPushNotificationCommand(
    Guid UserId,
    string Title,
    string Body,
    Dictionary<string, string> Data);
