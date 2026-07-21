using System.Text.Json;
using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.Models.Elasticsearch;
using MassTransit;
using RabbitMQ.Client;

namespace CraftoraApi.Infrastructure.Messaging;

public sealed class RabbitMqPublisher : IRabbitMqPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionFactory _connectionFactory;
    private readonly IBus _bus;
    private readonly ILogger<RabbitMqPublisher> _logger;

    public RabbitMqPublisher(
        IConnectionFactory connectionFactory,
        IBus bus,
        ILogger<RabbitMqPublisher> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PublishProductSyncMessage(
        ProductSyncMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: RabbitMqQueues.ProductSync,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: RabbitMqQueues.ProductSync,
            body: body,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Product sync message published. ProductId: {ProductId}, Action: {Action}",
            message.ProductId,
            message.Action);
    }

    public async Task PublishShopSyncMessage(
        ShopSyncMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await PublishSearchSyncMessageAsync(
            RabbitMqQueues.ShopSync,
            message,
            cancellationToken);

        _logger.LogInformation(
            "Shop sync message published. ShopId: {ShopId}, Action: {Action}",
            message.ShopId,
            message.Action);
    }

    public async Task PublishMediaSyncMessage(
        MediaSyncMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await PublishSearchSyncMessageAsync(
            RabbitMqQueues.MediaSync,
            message,
            cancellationToken);

        _logger.LogInformation(
            "Media sync message published. MediaId: {MediaId}, Action: {Action}",
            message.MediaId,
            message.Action);
    }

    public async Task PublishProcessVideoCommand(
        ProcessVideoCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _bus.Publish(command, cancellationToken);

        _logger.LogInformation(
            "Process video command published. VideoId: {VideoId}, TargetType: {TargetType}",
            command.VideoId,
            command.TargetType);
    }

    public async Task PublishPushNotificationCommand(
        SendPushNotificationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _bus.Publish(command, cancellationToken);

        _logger.LogInformation(
            "Push notification command published. UserId: {UserId}, Title: {Title}",
            command.UserId,
            command.Title);
    }

    public async Task PublishGenerateInvoiceCommand(
        GenerateInvoiceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _bus.Publish(command, cancellationToken);

        _logger.LogInformation(
            "Generate invoice command published. OrderId: {OrderId}, UserId: {UserId}",
            command.OrderId,
            command.UserId);
    }

    public async Task PublishSendEmailCommand(
        SendEmailCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await _bus.Publish(command, cancellationToken);

        _logger.LogInformation(
            "Send email command published. To: {To}, Subject: {Subject}",
            command.To,
            command.Subject);
    }

    private async Task PublishSearchSyncMessageAsync<TMessage>(
        string queueName,
        TMessage message,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: queueName,
            body: body,
            cancellationToken: cancellationToken);
    }
}
