namespace CraftoraApi.Infrastructure.Services;

public interface IPushNotificationService
{
    Task SendPushNotificationAsync(
        Guid notificationId,
        Guid userId,
        string title,
        string body,
        Dictionary<string, string> data,
        CancellationToken cancellationToken = default);
}
