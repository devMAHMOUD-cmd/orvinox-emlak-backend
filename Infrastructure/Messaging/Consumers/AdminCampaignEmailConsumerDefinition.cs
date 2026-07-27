using MassTransit;

namespace CraftoraApi.Infrastructure.Messaging.Consumers;

public sealed class AdminCampaignEmailConsumerDefinition
    : ConsumerDefinition<AdminCampaignEmailConsumer>
{
    public AdminCampaignEmailConsumerDefinition()
    {
        EndpointName = "AdminEmailCampaign";
        ConcurrentMessageLimit = 2;
    }
}
