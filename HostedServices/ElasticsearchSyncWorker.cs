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
        var retryDelay = TimeSpan.FromSeconds(5);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartRabbitConsumersAsync(stoppingToken);
                retryDelay = TimeSpan.FromSeconds(5);
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Elasticsearch sync worker RabbitMQ session failed. Retrying in {RetryDelay}.",
                    retryDelay);
            }

            await DisposeRabbitResourcesAsync();

            try
            {
                await Task.Delay(retryDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 30));
        }
    }

    private async Task StartRabbitConsumersAsync(CancellationToken cancellationToken)
    {
        _connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await StartConsumerAsync(RabbitMqQueues.ProductSync, HandleProductMessageAsync, cancellationToken);
        await StartConsumerAsync(RabbitMqQueues.ShopSync, HandleShopMessageAsync, cancellationToken);
        await StartConsumerAsync(RabbitMqQueues.MediaSync, HandleMediaMessageAsync, cancellationToken);

        _logger.LogInformation("Elasticsearch sync worker connected to RabbitMQ.");
    }

    private async Task StartConsumerAsync(
        string queueName,
        Func<BasicDeliverEventArgs, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        if (_channel is null)
        {
            return;
        }

        await _channel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, args) => await handler(args, cancellationToken);

        await _channel.BasicConsumeAsync(
            queue: queueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);
    }

    private async Task HandleProductMessageAsync(
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
            await NackForRetryAsync(args.DeliveryTag, cancellationToken);
        }
    }

    private async Task HandleShopMessageAsync(
        BasicDeliverEventArgs args,
        CancellationToken cancellationToken)
    {
        if (_channel is null)
        {
            return;
        }

        try
        {
            var message = JsonSerializer.Deserialize<ShopSyncMessage>(
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
                    throw new InvalidOperationException("Index message requires a shop document.");
                }

                await searchService.IndexShopAsync(message.Document, cancellationToken);
            }
            else if (string.Equals(message.Action, "Delete", StringComparison.OrdinalIgnoreCase))
            {
                await searchService.DeleteShopIndexAsync(message.ShopId, cancellationToken);
            }
            else
            {
                _logger.LogWarning(
                    "Unknown shop sync action received. ShopId: {ShopId}, Action: {Action}",
                    message.ShopId,
                    message.Action);
            }

            await _channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Shop sync message could not be processed.");
            await NackForRetryAsync(args.DeliveryTag, cancellationToken);
        }
    }

    private async Task HandleMediaMessageAsync(
        BasicDeliverEventArgs args,
        CancellationToken cancellationToken)
    {
        if (_channel is null)
        {
            return;
        }

        try
        {
            var message = JsonSerializer.Deserialize<MediaSyncMessage>(
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
                    throw new InvalidOperationException("Index message requires a media document.");
                }

                await searchService.IndexMediaAsync(message.Document, cancellationToken);
            }
            else if (string.Equals(message.Action, "Delete", StringComparison.OrdinalIgnoreCase))
            {
                await searchService.DeleteMediaIndexAsync(message.MediaId, cancellationToken);
            }
            else
            {
                _logger.LogWarning(
                    "Unknown media sync action received. MediaId: {MediaId}, Action: {Action}",
                    message.MediaId,
                    message.Action);
            }

            await _channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Media sync message could not be processed.");
            await NackForRetryAsync(args.DeliveryTag, cancellationToken);
        }
    }

    private async Task NackForRetryAsync(ulong deliveryTag, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || _channel is null)
        {
            return;
        }

        try
        {
            await _channel.BasicNackAsync(
                deliveryTag,
                multiple: false,
                requeue: true,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // RabbitMQ channel is closing during application shutdown.
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "RabbitMQ message could not be requeued. DeliveryTag: {DeliveryTag}",
                deliveryTag);
        }
    }

    private async Task DisposeRabbitResourcesAsync()
    {
        var channel = _channel;
        _channel = null;
        if (channel is not null)
        {
            try
            {
                await channel.DisposeAsync();
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "RabbitMQ channel cleanup failed.");
            }
        }

        var connection = _connection;
        _connection = null;
        if (connection is not null)
        {
            try
            {
                await connection.DisposeAsync();
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "RabbitMQ connection cleanup failed.");
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await DisposeRabbitResourcesAsync();
        await base.StopAsync(cancellationToken);
    }
}
