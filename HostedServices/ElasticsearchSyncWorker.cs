using System.Text.Json;
using CraftoraApi.Infrastructure.Messaging;
using CraftoraApi.Models.Elasticsearch;
using CraftoraApi.Services.Interfaces;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CraftoraApi.HostedServices;

public sealed class ElasticsearchSyncWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionFactory _connectionFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ElasticsearchSyncWorker> _logger;
    private IConnection? _connection;
    private IChannel? _channel;

    public ElasticsearchSyncWorker(
        IConnectionFactory connectionFactory,
        IServiceProvider serviceProvider,
        ILogger<ElasticsearchSyncWorker> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _connection = await _connectionFactory.CreateConnectionAsync(stoppingToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync(
            queue: RabbitMqQueues.ProductSync,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, args) => await HandleMessageAsync(args, stoppingToken);

        await _channel.BasicConsumeAsync(
            queue: RabbitMqQueues.ProductSync,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Application shutdown.
        }
    }

    private async Task HandleMessageAsync(
        BasicDeliverEventArgs args,
        CancellationToken cancellationToken)
    {
        if (_channel is null)
        {
            return;
        }

        try
        {
            var message = JsonSerializer.Deserialize<ProductSyncMessage>(
                args.Body.ToArray(),
                JsonOptions);
            if (message is null)
            {
                await _channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();

            if (string.Equals(message.Action, "Index", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Document is null)
                {
                    throw new InvalidOperationException("Index message requires a product document.");
                }

                await searchService.IndexProductAsync(message.Document, cancellationToken);
            }
            else if (string.Equals(message.Action, "Delete", StringComparison.OrdinalIgnoreCase))
            {
                await searchService.DeleteProductIndexAsync(message.ProductId, cancellationToken);
            }
            else
            {
                _logger.LogWarning(
                    "Unknown product sync action received. ProductId: {ProductId}, Action: {Action}",
                    message.ProductId,
                    message.Action);
            }

            await _channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Product sync message could not be processed.");
            await _channel.BasicNackAsync(
                args.DeliveryTag,
                multiple: false,
                requeue: true,
                cancellationToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken);
            await _channel.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.CloseAsync(cancellationToken);
            await _connection.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
