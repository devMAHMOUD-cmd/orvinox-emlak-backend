using System.Data.Common;
using System.Text.Json;
using CraftoraApi.Data;
using CraftoraApi.DTOs.Discovery;
using CraftoraApi.Infrastructure.Security;
using CraftoraApi.Middleware;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CraftoraApi.Services.Discovery;

public sealed class DiscoveryEventService : IDiscoveryEventService
{
    private const int MaxMetadataLength = 4096;
    private static readonly HashSet<string> AllowedEventTypes =
    [
        "impression",
        "playback_started",
        "playback_progress",
        "playback_ended",
        "playback_completed",
        "looped",
        "content_opened"
    ];

    private readonly AppDbContext _dbContext;
    private readonly IDiscoveryTrackingTokenService _trackingTokenService;

    public DiscoveryEventService(
        AppDbContext dbContext,
        IDiscoveryTrackingTokenService trackingTokenService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _trackingTokenService = trackingTokenService
            ?? throw new ArgumentNullException(nameof(trackingTokenService));
    }

    public async Task<DiscoveryEventBatchResponseDto> RecordBatchAsync(
        Guid userId,
        DiscoveryEventBatchRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Events is null || request.Events.Count is < 1 or > 50)
        {
            throw new BadRequestException("Discovery event paketi 1 ile 50 olay icermelidir.");
        }

        var validatedEvents = request.Events
            .Select(item => ValidateEvent(userId, item))
            .ToList();
        var shopIds = validatedEvents
            .Select(item => item.Context.ShopId)
            .Distinct()
            .ToList();
        var ownedShopIds = await _dbContext.Shops
            .AsNoTracking()
            .Where(shop => shop.UserId == userId && shopIds.Contains(shop.Id))
            .Select(shop => shop.Id)
            .ToHashSetAsync(cancellationToken);

        var acceptedCount = 0;
        var duplicateCount = 0;
        var ignoredCount = 0;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var validatedEvent in validatedEvents)
        {
            if (ownedShopIds.Contains(validatedEvent.Context.ShopId))
            {
                ignoredCount++;
                continue;
            }

            var inserted = await RecordEventAsync(
                userId,
                validatedEvent.Request,
                validatedEvent.EventType,
                validatedEvent.Context,
                cancellationToken);
            if (inserted)
            {
                acceptedCount++;
            }
            else
            {
                duplicateCount++;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new DiscoveryEventBatchResponseDto(
            acceptedCount,
            duplicateCount,
            ignoredCount);
    }

    public async Task<DiscoveryFeedbackResponseDto> SetFeedbackAsync(
        Guid userId,
        DiscoveryFeedbackRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.EventId == Guid.Empty)
        {
            throw new BadRequestException("Discovery feedback eventId zorunludur.");
        }

        var feedbackType = NormalizeFeedbackType(request.FeedbackType);
        if (!_trackingTokenService.TryValidate(
                request.TrackingToken,
                userId,
                out var context))
        {
            throw new BadRequestException("Discovery tracking token gecersiz veya suresi dolmus.");
        }

        var ownsShop = await _dbContext.Shops
            .AsNoTracking()
            .AnyAsync(
                shop => shop.Id == context.ShopId && shop.UserId == userId,
                cancellationToken);
        if (ownsShop)
        {
            throw new BadRequestException("Kendi magazaniz icin discovery geri bildirimi veremezsiniz.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var feedback = await SetFeedbackRecordAsync(
            userId,
            feedbackType,
            context,
            cancellationToken);

        var feedbackEvent = new DiscoveryEventRequestDto(
            request.EventId,
            feedbackType,
            request.TrackingToken,
            DwellMs: null,
            CompletionRate: null,
            VisiblePercentage: null,
            Metadata: null);
        _ = await RecordEventAsync(
            userId,
            feedbackEvent,
            feedbackType,
            context,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return feedback;
    }

    public async Task<IReadOnlyList<DiscoveryFeedbackResponseDto>> GetFeedbackAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var connection = _dbContext.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere)
        {
            await _dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    feedback_id,
                    feedback_type,
                    content_type,
                    content_id,
                    shop_id,
                    expires_at,
                    created_at
                FROM public.get_active_discovery_feedback(CAST(@user_id AS uuid))
                """;
            AddParameter(command, "user_id", userId);

            var feedback = new List<DiscoveryFeedbackResponseDto>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                feedback.Add(MapFeedback(reader));
            }

            return feedback;
        }
        finally
        {
            if (openedHere)
            {
                await _dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    public async Task RemoveFeedbackAsync(
        Guid userId,
        Guid feedbackId,
        CancellationToken cancellationToken = default)
    {
        if (feedbackId == Guid.Empty)
        {
            throw new BadRequestException("Discovery feedback kimligi gecersiz.");
        }

        var result = await ExecuteScalarFunctionAsync(
            "SELECT public.remove_discovery_feedback(CAST(@user_id AS uuid), CAST(@feedback_id AS uuid))",
            [
                ("user_id", userId),
                ("feedback_id", feedbackId)
            ],
            cancellationToken);
        if (result is not true)
        {
            throw new NotFoundException("Discovery geri bildirimi bulunamadi.");
        }
    }

    private ValidatedEvent ValidateEvent(Guid userId, DiscoveryEventRequestDto request)
    {
        if (request.EventId == Guid.Empty)
        {
            throw new BadRequestException("Discovery eventId zorunludur.");
        }

        var eventType = request.EventType?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!AllowedEventTypes.Contains(eventType))
        {
            throw new BadRequestException("Gecersiz discovery eventType degeri.");
        }

        if (!_trackingTokenService.TryValidate(
                request.TrackingToken,
                userId,
                out var context))
        {
            throw new BadRequestException("Discovery tracking token gecersiz veya suresi dolmus.");
        }

        if (request.DwellMs is < 0 or > 21_600_000 ||
            request.CompletionRate is < 0 or > 1 ||
            request.VisiblePercentage is < 0 or > 100)
        {
            throw new BadRequestException("Discovery event metrikleri gecersiz.");
        }

        if (eventType == "impression" &&
            (request.DwellMs is null or < 500 ||
             request.VisiblePercentage is null or < 50))
        {
            throw new BadRequestException(
                "Impression icin en az 500 ms ve yuzde 50 gorunurluk gereklidir.");
        }

        if ((eventType is "playback_progress" or "playback_ended") &&
            request.DwellMs is null)
        {
            throw new BadRequestException($"{eventType} icin dwellMs zorunludur.");
        }

        if ((eventType is "playback_completed" or "looped") &&
            request.CompletionRate is null or < 0.9m)
        {
            throw new BadRequestException(
                $"{eventType} icin completionRate en az 0.9 olmalidir.");
        }

        ValidateMetadata(request.Metadata);
        return new ValidatedEvent(request, eventType, context);
    }

    private async Task<bool> RecordEventAsync(
        Guid userId,
        DiscoveryEventRequestDto request,
        string eventType,
        DiscoveryTrackingContext context,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT event_record_id, was_inserted
            FROM public.record_discovery_event(
                CAST(@event_id AS uuid),
                CAST(@user_id AS uuid),
                CAST(@feed_session_id AS uuid),
                CAST(@tracking_token_id AS uuid),
                CAST(@content_type AS text),
                CAST(@content_id AS uuid),
                CAST(@shop_id AS uuid),
                CAST(@event_type AS text),
                CAST(@position AS integer),
                CAST(@algorithm_version AS text),
                CAST(@dwell_ms AS integer),
                CAST(@completion_rate AS numeric),
                CAST(@visible_percentage AS integer),
                CAST(@metadata AS jsonb))
            """;
        AddParameter(command, "event_id", request.EventId);
        AddParameter(command, "user_id", userId);
        AddParameter(command, "feed_session_id", context.FeedSessionId);
        AddParameter(command, "tracking_token_id", context.TokenId);
        AddParameter(command, "content_type", context.ContentType);
        AddParameter(command, "content_id", context.ContentId);
        AddParameter(command, "shop_id", context.ShopId);
        AddParameter(command, "event_type", eventType);
        AddParameter(command, "position", context.Position);
        AddParameter(command, "algorithm_version", context.AlgorithmVersion);
        AddParameter(command, "dwell_ms", request.DwellMs);
        AddParameter(command, "completion_rate", request.CompletionRate);
        AddParameter(command, "visible_percentage", request.VisiblePercentage);
        AddParameter(command, "metadata", SerializeMetadata(request.Metadata));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Discovery event function returned no result.");
        }

        return reader.GetBoolean(1);
    }

    private async Task<DiscoveryFeedbackResponseDto> SetFeedbackRecordAsync(
        Guid userId,
        string feedbackType,
        DiscoveryTrackingContext context,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT
                feedback_id,
                result_feedback_type,
                result_content_type,
                result_content_id,
                result_shop_id,
                result_expires_at,
                result_created_at
            FROM public.set_discovery_feedback(
                CAST(@user_id AS uuid),
                CAST(@feedback_type AS text),
                CAST(@content_type AS text),
                CAST(@content_id AS uuid),
                CAST(@shop_id AS uuid))
            """;
        AddParameter(command, "user_id", userId);
        AddParameter(command, "feedback_type", feedbackType);
        AddParameter(command, "content_type", context.ContentType);
        AddParameter(command, "content_id", context.ContentId);
        AddParameter(command, "shop_id", context.ShopId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Discovery feedback function returned no result.");
        }

        return MapFeedback(reader);
    }

    private async Task<object?> ExecuteScalarFunctionAsync(
        string commandText,
        IReadOnlyList<(string Name, object? Value)> parameters,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere)
        {
            await _dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = commandText;
            foreach (var parameter in parameters)
            {
                AddParameter(command, parameter.Name, parameter.Value);
            }

            return await command.ExecuteScalarAsync(cancellationToken);
        }
        finally
        {
            if (openedHere)
            {
                await _dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    private static DiscoveryFeedbackResponseDto MapFeedback(DbDataReader reader)
    {
        return new DiscoveryFeedbackResponseDto(
            Id: reader.GetGuid(0),
            FeedbackType: reader.GetString(1),
            ContentType: reader.IsDBNull(2) ? null : reader.GetString(2),
            ContentId: reader.IsDBNull(3) ? null : reader.GetGuid(3),
            ShopId: reader.IsDBNull(4) ? null : reader.GetGuid(4),
            ExpiresAt: reader.IsDBNull(5) ? null : reader.GetDateTime(5),
            CreatedAt: reader.GetDateTime(6));
    }

    private static string NormalizeFeedbackType(string feedbackType)
    {
        return feedbackType?.Trim().ToLowerInvariant() switch
        {
            "not_interested" => "not_interested",
            "hide_shop" => "hide_shop",
            _ => throw new BadRequestException("Gecersiz discovery feedbackType degeri.")
        };
    }

    private static void ValidateMetadata(Dictionary<string, JsonElement>? metadata)
    {
        if (metadata is null)
        {
            return;
        }

        if (metadata.Any(item =>
            PlainTextInputValidator.ContainsProhibitedContent(item.Key) ||
            JsonInputSafetyValidator.ContainsProhibitedContent(item.Value)))
        {
            throw new BadRequestException("Discovery metadata guvensiz icerik barindiriyor.");
        }

        if (SerializeMetadata(metadata).Length > MaxMetadataLength)
        {
            throw new BadRequestException("Discovery metadata en fazla 4096 karakter olabilir.");
        }
    }

    private static string SerializeMetadata(Dictionary<string, JsonElement>? metadata)
    {
        return metadata is null || metadata.Count == 0
            ? "{}"
            : JsonSerializer.Serialize(metadata);
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private sealed record ValidatedEvent(
        DiscoveryEventRequestDto Request,
        string EventType,
        DiscoveryTrackingContext Context);
}
