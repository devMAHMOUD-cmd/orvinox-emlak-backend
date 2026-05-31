using System.Text;
using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.Services.Interfaces;
using FFMpegCore;

namespace CraftoraApi.Infrastructure.Services;

public sealed class VideoProcessingService : IVideoProcessingService
{
    private readonly IStorageService _storageService;
    private readonly ILogger<VideoProcessingService> _logger;

    public VideoProcessingService(
        IStorageService storageService,
        ILogger<VideoProcessingService> logger)
    {
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<VideoProcessingResult> ProcessVideoAsync(
        ProcessVideoCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var baseObjectKey = string.Equals(command.TargetType, "Media", StringComparison.OrdinalIgnoreCase)
            ? $"media/{command.VideoId}"
            : $"courses/{command.CourseId}/videos/{command.VideoId}";
        var manifestObjectKey = $"{baseObjectKey}/master.m3u8";
        var thumbnailObjectKey = $"{baseObjectKey}/thumbnail.jpg";

        if (File.Exists(command.OriginalFileUrl))
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "craftora-video", command.VideoId.ToString("D"));
            Directory.CreateDirectory(tempDirectory);

            var manifestPath = Path.Combine(tempDirectory, "master.m3u8");
            var thumbnailPath = Path.Combine(tempDirectory, "thumbnail.jpg");

            await FFMpegArguments
                .FromFileInput(command.OriginalFileUrl)
                .OutputToFile(manifestPath, overwrite: true, options => options
                    .WithCustomArgument("-vf scale=-2:720 -codec:v libx264 -codec:a aac -hls_time 6 -hls_playlist_type vod"))
                .ProcessAsynchronously();

            await FFMpegArguments
                .FromFileInput(command.OriginalFileUrl)
                .OutputToFile(thumbnailPath, overwrite: true, options => options
                    .WithCustomArgument("-frames:v 1 -q:v 2"))
                .ProcessAsynchronously();

            await _storageService.UploadFileAsync(
                "private-products",
                manifestObjectKey,
                await File.ReadAllBytesAsync(manifestPath, cancellationToken),
                "application/vnd.apple.mpegurl",
                cancellationToken);

            if (File.Exists(thumbnailPath))
            {
                await _storageService.UploadFileAsync(
                    "public-assets",
                    thumbnailObjectKey,
                    await File.ReadAllBytesAsync(thumbnailPath, cancellationToken),
                    "image/jpeg",
                    cancellationToken);
            }

            Directory.Delete(tempDirectory, recursive: true);
        }
        else
        {
            _logger.LogInformation(
                "Original video file is not local. Video processing mocked. VideoId: {VideoId}, OriginalFileUrl: {OriginalFileUrl}",
                command.VideoId,
                command.OriginalFileUrl);

            await _storageService.UploadFileAsync(
                "private-products",
                manifestObjectKey,
                Encoding.UTF8.GetBytes("#EXTM3U\n# Craftora processing placeholder\n"),
                "application/vnd.apple.mpegurl",
                cancellationToken);
        }

        var videoUrl = _storageService.GeneratePresignedDownloadUrl("private-products", manifestObjectKey, 60 * 24);
        var thumbnailUrl = _storageService.GeneratePresignedDownloadUrl("public-assets", thumbnailObjectKey, 60 * 24);

        return new VideoProcessingResult(videoUrl, thumbnailUrl);
    }
}
