using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.Infrastructure.Services;
using MassTransit;

namespace CraftoraApi.Infrastructure.Messaging.Consumers;

public sealed class NotificationConsumer : IConsumer<SendPushNotificationCommand>
{
    private readonly IPushNotificationService _pushNotificationService;
    private readonly ILogger<NotificationConsumer> _logger;

    public NotificationConsumer(
        IPushNotificationService pushNotificationService,
        ILogger<NotificationConsumer> logger)
    {
        _pushNotificationService = pushNotificationService ?? throw new ArgumentNullException(nameof(pushNotificationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Consume(ConsumeContext<SendPushNotificationCommand> context)
    {
        var message = context.Message;

        await _pushNotificationService.SendPushNotificationAsync(
            message.NotificationId,
            message.UserId,
            message.Title,
            message.Body,
            message.Data,
            context.CancellationToken);

        _logger.LogInformation("Push notification command consumed. UserId: {UserId}", message.UserId);
    }
}
