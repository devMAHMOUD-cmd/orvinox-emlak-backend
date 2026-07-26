using System.Data;
using System.Data.Common;
using CraftoraApi.Data;
using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.Infrastructure.Services;
using CraftoraApi.Redis;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Infrastructure.Messaging.Consumers;

public sealed class VideoProcessConsumer : IConsumer<ProcessVideoCommand>
{
    private readonly IVideoProcessingService _videoProcessingService;
    private readonly AppDbContext _dbContext;
    private readonly ICacheService _cacheService;
    private readonly ILogger<VideoProcessConsumer> _logger;

    public VideoProcessConsumer(
        IVideoProcessingService videoProcessingService,
        AppDbContext dbContext,
        ICacheService cacheService,
        ILogger<VideoProcessConsumer> logger)
    {
        _videoProcessingService = videoProcessingService ?? throw new ArgumentNullException(nameof(videoProcessingService));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Consume(ConsumeContext<ProcessVideoCommand> context)
    {
        var message = context.Message;
        var result = await _videoProcessingService.ProcessVideoAsync(message, context.CancellationToken);

        if (string.Equals(message.TargetType, "Media", StringComparison.OrdinalIgnoreCase))
        {
            await UpdateMediaAsync(message.VideoId, result, context.CancellationToken);
            return;
        }

        await UpdateCourseLessonAsync(message, result, context.CancellationToken);
    }

    private async Task UpdateMediaAsync(
        Guid mediaId,
        VideoProcessingResult result,
        CancellationToken cancellationToken)
    {
        var updated = await CompleteMediaProcessingAsync(
            mediaId,
            result.VideoUrl,
            result.ThumbnailUrl,
            cancellationToken);
        if (!updated)
        {
            _logger.LogWarning("Processed video has no matching media row. MediaId: {MediaId}", mediaId);
            return;
        }

        await _cacheService.IncrementAsync("media:feed:contract:v2:version", cancellationToken: cancellationToken);
        await _cacheService.IncrementAsync("media:liked:contract:v1:version", cancellationToken: cancellationToken);

        _logger.LogInformation("Media video processed. MediaId: {MediaId}", mediaId);
    }

    private async Task<bool> CompleteMediaProcessingAsync(
        Guid mediaId,
        string videoUrl,
        string? thumbnailUrl,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await _dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT public.complete_media_processing(
                    CAST(@media_id AS uuid),
                    CAST(@video_url AS text),
                    CAST(@thumbnail_url AS text))
                """;
            AddParameter(command, "media_id", mediaId);
            AddParameter(command, "video_url", videoUrl);
            AddParameter(command, "thumbnail_url", thumbnailUrl);

            return await command.ExecuteScalarAsync(cancellationToken) is true;
        }
        finally
        {
            if (openedHere)
            {
                await _dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private async Task UpdateCourseLessonAsync(
        ProcessVideoCommand message,
        VideoProcessingResult result,
        CancellationToken cancellationToken)
    {
        var lesson = await _dbContext.CourseLessons
            .Include(lesson => lesson.CourseSection)
            .FirstOrDefaultAsync(
                lesson =>
                    lesson.Id == message.VideoId &&
                    lesson.CourseSection.CourseId == message.CourseId,
                cancellationToken);

        if (lesson is null)
        {
            _logger.LogWarning(
                "Processed video has no matching course lesson. VideoId: {VideoId}, CourseId: {CourseId}",
                message.VideoId,
                message.CourseId);
            return;
        }

        lesson.VideoUrl = result.VideoUrl;
        lesson.IsActive = true;
        lesson.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Video processed and course lesson updated. LessonId: {LessonId}, CourseId: {CourseId}",
            lesson.Id,
            message.CourseId);
    }
}
