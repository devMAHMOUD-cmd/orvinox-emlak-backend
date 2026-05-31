using CraftoraApi.Data;
using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.Infrastructure.Services;
using CraftoraApi.Models.Enums;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Infrastructure.Messaging.Consumers;

public sealed class VideoProcessConsumer : IConsumer<ProcessVideoCommand>
{
    private readonly IVideoProcessingService _videoProcessingService;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<VideoProcessConsumer> _logger;

    public VideoProcessConsumer(
        IVideoProcessingService videoProcessingService,
        AppDbContext dbContext,
        ILogger<VideoProcessConsumer> logger)
    {
        _videoProcessingService = videoProcessingService ?? throw new ArgumentNullException(nameof(videoProcessingService));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
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
        var media = await _dbContext.Media.FirstOrDefaultAsync(
            media => media.Id == mediaId,
            cancellationToken);

        if (media is null)
        {
            _logger.LogWarning("Processed video has no matching media row. MediaId: {MediaId}", mediaId);
            return;
        }

        media.VideoUrl = result.VideoUrl;
        media.ThumbnailUrl = result.ThumbnailUrl;
        media.Status = MediaStatus.Ready;
        media.IsActive = true;
        media.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Media video processed. MediaId: {MediaId}", mediaId);
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
