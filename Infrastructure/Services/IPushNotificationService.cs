namespace CraftoraApi.Infrastructure.Services;

public interface IPushNotificationService
{
    Task SendPushNotificationAsync(
        Guid userId,
        string title,
        string body,
        Dictionary<string, string> data,
        CancellationToken cancellationToken = default);
}
