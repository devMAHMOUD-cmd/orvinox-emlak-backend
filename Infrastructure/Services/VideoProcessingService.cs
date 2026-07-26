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

            _logger.LogInformation(
                "Video source is already in object storage; original object key retained. VideoId: {VideoId}, ObjectKey: {ObjectKey}",
                command.VideoId,
                existingObjectKey);

            return new VideoProcessingResult(existingObjectKey, null);
        }

        var baseObjectKey = string.Equals(command.TargetType, "Media", StringComparison.OrdinalIgnoreCase)
            ? $"media/{command.VideoId}"
            : $"courses/{command.CourseId}/videos/{command.VideoId}";
        var videoExtension = Path.GetExtension(command.OriginalFileUrl);
        if (string.IsNullOrWhiteSpace(videoExtension))
        {
            videoExtension = ".mp4";
        }

        var originalObjectKey = $"{baseObjectKey}/original{videoExtension.ToLowerInvariant()}";
        var thumbnailObjectKey = $"{baseObjectKey}/thumbnail.jpg";
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "craftora-video",
            command.VideoId.ToString("D"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var thumbnailPath = Path.Combine(tempDirectory, "thumbnail.jpg");

            await using (var videoStream = File.OpenRead(command.OriginalFileUrl))
            {
                await _storageService.UploadFileAsync(
                    PrivateProductsBucketName,
                    originalObjectKey,
                    videoStream,
                    GetVideoContentType(videoExtension),
                    cancellationToken);
            }

            await FFMpegArguments
                .FromFileInput(command.OriginalFileUrl)
                .OutputToFile(thumbnailPath, overwrite: true, options => options
                    .WithCustomArgument("-frames:v 1 -q:v 2"))
                .ProcessAsynchronously();

            if (File.Exists(thumbnailPath))
            {
                await using var thumbnailStream = File.OpenRead(thumbnailPath);
                await _storageService.UploadFileAsync(
                    PublicAssetsBucketName,
                    thumbnailObjectKey,
                    thumbnailStream,
                    "image/jpeg",
                    cancellationToken);
            }

            return new VideoProcessingResult(originalObjectKey, thumbnailObjectKey);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
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
