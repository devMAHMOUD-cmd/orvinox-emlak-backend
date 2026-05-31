using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.Models.Elasticsearch;

namespace CraftoraApi.Infrastructure.Messaging;

public interface IRabbitMqPublisher
{
    Task PublishProductSyncMessage(
        ProductSyncMessage message,
        CancellationToken cancellationToken = default);

    Task PublishProcessVideoCommand(
        ProcessVideoCommand command,
        CancellationToken cancellationToken = default);

    Task PublishPushNotificationCommand(
        SendPushNotificationCommand command,
        CancellationToken cancellationToken = default);

    Task PublishGenerateInvoiceCommand(
        GenerateInvoiceCommand command,
        CancellationToken cancellationToken = default);
}
