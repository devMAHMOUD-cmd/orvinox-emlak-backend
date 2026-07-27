using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.Services.Interfaces;
using MassTransit;

namespace CraftoraApi.Infrastructure.Messaging.Consumers;

public sealed class AdminCampaignEmailConsumer : IConsumer<SendAdminCampaignEmailCommand>
{
    private readonly IAdminCampaignEmailDeliveryService _deliveryService;
    private readonly ILogger<AdminCampaignEmailConsumer> _logger;

    public AdminCampaignEmailConsumer(
        IAdminCampaignEmailDeliveryService deliveryService,
        ILogger<AdminCampaignEmailConsumer> logger)
    {
        _deliveryService = deliveryService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SendAdminCampaignEmailCommand> context)
    {
        _logger.LogInformation(
            "Admin campaign recipient received. RecipientId: {RecipientId}, MessageId: {MessageId}",
            context.Message.RecipientId,
            context.MessageId);

        await _deliveryService.DeliverAsync(
            context.Message.RecipientId,
            context.CancellationToken);
    }
}
