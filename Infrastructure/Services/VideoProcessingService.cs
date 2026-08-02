using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.Services.Interfaces;
using FFMpegCore;

namespace CraftoraApi.Infrastructure.Services;

public sealed class VideoProcessingService : IVideoProcessingService
{
    private const string PrivateProductsBucketName = "private-products";
    private const string PublicAssetsBucketName = "public-assets";

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
        var thumbnailObjectKey = $"{baseObjectKey}/thumbnail.jpg";
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "craftora-video",
            command.VideoId.ToString("D"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            if (!File.Exists(command.OriginalFileUrl))
            {
                var existingObjectKey = ExtractObjectKey(
                    command.OriginalFileUrl,
                    PrivateProductsBucketName);
                var objectInfo = await _storageService.GetObjectInfoAsync(
                    PrivateProductsBucketName,
                    existingObjectKey,
                    cancellationToken);
                if (objectInfo is null)
                {
                    throw new FileNotFoundException(
                        "Video source object was not found in storage.",
                        existingObjectKey);
                }

                if (!command.GenerateThumbnail)
                {
                    return new VideoProcessingResult(existingObjectKey, null);
                }

                var sourceExtension = Path.GetExtension(existingObjectKey);
                var sourcePath = Path.Combine(
                    tempDirectory,
                    $"source{(string.IsNullOrWhiteSpace(sourceExtension) ? ".mp4" : sourceExtension)}");
                await _storageService.DownloadFileAsync(
                    PrivateProductsBucketName,
                    existingObjectKey,
                    sourcePath,
                    cancellationToken);
                await GenerateAndUploadThumbnailAsync(
                    sourcePath,
                    thumbnailObjectKey,
                    tempDirectory,
                    cancellationToken);

                _logger.LogInformation(
                    "Automatic video thumbnail generated. VideoId: {VideoId}, ObjectKey: {ObjectKey}",
                    command.VideoId,
                    thumbnailObjectKey);

                return new VideoProcessingResult(existingObjectKey, thumbnailObjectKey);
            }

            var videoExtension = Path.GetExtension(command.OriginalFileUrl);
            if (string.IsNullOrWhiteSpace(videoExtension))
            {
                videoExtension = ".mp4";
            }
            var originalObjectKey = $"{baseObjectKey}/original{videoExtension.ToLowerInvariant()}";

            await using (var videoStream = File.OpenRead(command.OriginalFileUrl))
            {
                await _storageService.UploadFileAsync(
                    PrivateProductsBucketName,
                    originalObjectKey,
                    videoStream,
                    GetVideoContentType(videoExtension),
                    cancellationToken);
            }

            if (command.GenerateThumbnail)
            {
                await GenerateAndUploadThumbnailAsync(
                    command.OriginalFileUrl,
                    thumbnailObjectKey,
                    tempDirectory,
                    cancellationToken);
            }

            return new VideoProcessingResult(
                originalObjectKey,
                command.GenerateThumbnail ? thumbnailObjectKey : null);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private async Task GenerateAndUploadThumbnailAsync(
        string sourcePath,
        string thumbnailObjectKey,
        string tempDirectory,
        CancellationToken cancellationToken)
    {
        var thumbnailPath = Path.Combine(tempDirectory, "thumbnail.jpg");
        await FFMpegArguments
            .FromFileInput(sourcePath)
            .OutputToFile(thumbnailPath, overwrite: true, options => options
                .WithCustomArgument("-vf thumbnail=30,scale=720:-2 -frames:v 1 -q:v 2"))
            .ProcessAsynchronously();

        if (!File.Exists(thumbnailPath))
        {
            throw new InvalidOperationException("Video thumbnail could not be generated.");
        }

        await using var thumbnailStream = File.OpenRead(thumbnailPath);
        await _storageService.UploadFileAsync(
            PublicAssetsBucketName,
            thumbnailObjectKey,
            thumbnailStream,
            "image/jpeg",
            cancellationToken);
    }

    private static string ExtractObjectKey(string urlOrObjectKey, string bucketName)
    {
        if (!Uri.TryCreate(urlOrObjectKey, UriKind.Absolute, out var uri))
        {
            return urlOrObjectKey.TrimStart('/');
        }

        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        var bucketPrefix = $"{bucketName}/";
        var bucketIndex = path.IndexOf(bucketPrefix, StringComparison.OrdinalIgnoreCase);

        return bucketIndex >= 0
            ? path[(bucketIndex + bucketPrefix.Length)..]
            : path;
    }

    private static string GetVideoContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".m4v" => "video/x-m4v",
            _ => "video/mp4"
        };
    }
}
