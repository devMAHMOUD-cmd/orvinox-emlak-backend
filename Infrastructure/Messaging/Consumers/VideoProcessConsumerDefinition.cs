using MassTransit;

namespace CraftoraApi.Infrastructure.Messaging.Consumers;

public sealed class VideoProcessConsumerDefinition : ConsumerDefinition<VideoProcessConsumer>
{
    public VideoProcessConsumerDefinition()
    {
        ConcurrentMessageLimit = 1;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<VideoProcessConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.PrefetchCount = 1;
    }
}
