using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.Services.Interfaces;
using FFMpegCore;
using System.Diagnostics;

namespace CraftoraApi.Infrastructure.Services;

public sealed class VideoProcessingService : IVideoProcessingService
{
    private const string PrivateProductsBucketName = "private-products";
    private const string PublicAssetsBucketName = "public-assets";
    private const string MediaStreamsBucketName = "media-streams";

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
            command.VideoId.ToString("D"),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var isMedia = string.Equals(command.TargetType, "Media", StringComparison.OrdinalIgnoreCase);
            string sourcePath;
            string existingObjectKey;

            if (!File.Exists(command.OriginalFileUrl))
            {
                existingObjectKey = ExtractObjectKey(
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

                var sourceExtension = Path.GetExtension(existingObjectKey);
                sourcePath = Path.Combine(
                    tempDirectory,
                    $"source{(string.IsNullOrWhiteSpace(sourceExtension) ? ".mp4" : sourceExtension)}");
                await _storageService.DownloadFileAsync(
                    PrivateProductsBucketName,
                    existingObjectKey,
                    sourcePath,
                    cancellationToken);

                if (isMedia)
                {
                    return await ProcessPublicMediaAsync(
                        command,
                        sourcePath,
                        thumbnailObjectKey,
                        tempDirectory,
                        cancellationToken);
                }

                if (command.GenerateThumbnail)
                {
                    await GenerateAndUploadThumbnailAsync(
                        sourcePath,
                        thumbnailObjectKey,
                        tempDirectory,
                        cancellationToken);
                }

                _logger.LogInformation(
                    "Automatic video thumbnail generated. VideoId: {VideoId}, ObjectKey: {ObjectKey}",
                    command.VideoId,
                    thumbnailObjectKey);

                return new VideoProcessingResult(
                    existingObjectKey,
                    command.GenerateThumbnail ? thumbnailObjectKey : null);
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

            if (isMedia)
            {
                return await ProcessPublicMediaAsync(
                    command,
                    command.OriginalFileUrl,
                    thumbnailObjectKey,
                    tempDirectory,
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
                try
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Temporary video processing directory could not be removed. Directory: {Directory}",
                        tempDirectory);
                }
            }
        }
    }

    private async Task<VideoProcessingResult> ProcessPublicMediaAsync(
        ProcessVideoCommand command,
        string sourcePath,
        string thumbnailObjectKey,
        string tempDirectory,
        CancellationToken cancellationToken)
    {
        var outputDirectory = Path.Combine(tempDirectory, "stream");
        Directory.CreateDirectory(outputDirectory);
        var fallbackPath = Path.Combine(outputDirectory, "fallback.mp4");

        await RunFfmpegAsync(
            [
                "-y", "-i", sourcePath,
                "-map", "0:v:0", "-map", "0:a:0?",
                "-vf", "scale=720:1280:force_original_aspect_ratio=decrease:force_divisible_by=2,pad=720:1280:(ow-iw)/2:(oh-ih)/2:black",
                "-c:v", "libx264", "-preset", "veryfast", "-profile:v", "main",
                "-level", "3.1", "-pix_fmt", "yuv420p", "-r", "30",
                "-crf", "23", "-maxrate", "2500k", "-bufsize", "5000k",
                "-c:a", "aac", "-b:a", "128k", "-ac", "2", "-ar", "48000",
                "-movflags", "+faststart", fallbackPath
            ],
            cancellationToken);

        var variants = new[]
        {
            new HlsVariant("360p", 360, 640, "800k", "1200k", "96k", 950_000),
            new HlsVariant("540p", 540, 960, "1400k", "2400k", "112k", 1_600_000),
            new HlsVariant("720p", 720, 1280, "2400k", "4800k", "128k", 2_700_000)
        };

        foreach (var variant in variants)
        {
            var playlistPath = Path.Combine(outputDirectory, $"{variant.Name}.m3u8");
            var segmentPattern = Path.Combine(outputDirectory, $"{variant.Name}_%03d.ts");
            await RunFfmpegAsync(
                [
                    "-y", "-i", sourcePath,
                    "-map", "0:v:0", "-map", "0:a:0?",
                    "-vf", $"scale={variant.Width}:{variant.Height}:force_original_aspect_ratio=decrease:force_divisible_by=2,pad={variant.Width}:{variant.Height}:(ow-iw)/2:(oh-ih)/2:black",
                    "-c:v", "libx264", "-preset", "veryfast", "-profile:v", "main",
                    "-level", "3.1", "-pix_fmt", "yuv420p", "-r", "30",
                    "-maxrate", variant.MaxRate, "-bufsize", variant.BufferSize,
                    "-crf", "23", "-g", "120", "-keyint_min", "120", "-sc_threshold", "0",
                    "-c:a", "aac", "-b:a", variant.AudioRate, "-ac", "2", "-ar", "48000",
                    "-f", "hls", "-hls_time", "4", "-hls_playlist_type", "vod",
                    "-hls_flags", "independent_segments", "-hls_segment_filename", segmentPattern,
                    playlistPath
                ],
                cancellationToken);
        }

        var masterPath = Path.Combine(outputDirectory, "master.m3u8");
        await File.WriteAllLinesAsync(
            masterPath,
            new[]
            {
                "#EXTM3U",
                "#EXT-X-VERSION:3",
                "#EXT-X-INDEPENDENT-SEGMENTS"
            }.Concat(variants.SelectMany(variant => new[]
            {
                $"#EXT-X-STREAM-INF:BANDWIDTH={variant.Bandwidth},RESOLUTION={variant.Width}x{variant.Height}",
                $"{variant.Name}.m3u8"
            })),
            cancellationToken);

        if (command.GenerateThumbnail)
        {
            await GenerateAndUploadThumbnailAsync(
                sourcePath,
                thumbnailObjectKey,
                tempDirectory,
                cancellationToken);
        }

        var version = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var objectPrefix = $"media/{command.VideoId:D}/{version}";
        foreach (var filePath in Directory.EnumerateFiles(outputDirectory))
        {
            var fileName = Path.GetFileName(filePath);
            var contentType = Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".m3u8" => "application/vnd.apple.mpegurl",
                ".ts" => "video/mp2t",
                _ => "video/mp4"
            };
            await using var stream = File.OpenRead(filePath);
            await _storageService.UploadCacheableFileAsync(
                MediaStreamsBucketName,
                $"{objectPrefix}/{fileName}",
                stream,
                contentType,
                "public, max-age=31536000, immutable",
                cancellationToken);
        }

        var analysis = await FFProbe.AnalyseAsync(sourcePath);
        return new VideoProcessingResult(
            OptimizedVideoUrl: $"{objectPrefix}/fallback.mp4",
            ThumbnailUrl: command.GenerateThumbnail ? thumbnailObjectKey : null,
            HlsUrl: $"{objectPrefix}/master.m3u8",
            DurationSeconds: Math.Max(1, (int)Math.Ceiling(analysis.Duration.TotalSeconds)));
    }

    private static async Task RunFfmpegAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var standardError = await standardErrorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"FFmpeg exited with code {process.ExitCode}: {standardError[^Math.Min(2000, standardError.Length)..]}");
        }
    }

    private sealed record HlsVariant(
        string Name,
        int Width,
        int Height,
        string MaxRate,
        string BufferSize,
        string AudioRate,
        int Bandwidth);

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
