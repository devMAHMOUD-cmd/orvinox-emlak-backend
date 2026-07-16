using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Runtime;
using CraftoraApi.Configuration;
using CraftoraApi.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace CraftoraApi.Services;

public sealed class S3StorageService : IStorageService, IDisposable
{
    private static readonly string[] RequiredBuckets =
    {
        "public-assets",
        "private-products",
        "invoices"
    };

    private readonly IAmazonS3 _s3Client;
    private readonly IAmazonS3 _presignClient;
    private readonly bool _ownsPresignClient;
    private readonly StorageSettings _settings;
    private readonly ILogger<S3StorageService> _logger;

    public S3StorageService(
        IAmazonS3 s3Client,
        IOptions<StorageSettings> storageOptions,
        ILogger<S3StorageService> logger)
    {
        _s3Client = s3Client ?? throw new ArgumentNullException(nameof(s3Client));
        _settings = storageOptions?.Value ?? throw new ArgumentNullException(nameof(storageOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _presignClient = CreatePresignClient(_settings, _s3Client, out _ownsPresignClient);
    }

    public async Task InitializeBucketsAsync()
    {
        var bucketResponse = await _s3Client.ListBucketsAsync();
        var existingBucketNames = (bucketResponse.Buckets ?? [])
            .Select(bucket => bucket.BucketName)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var bucketName in RequiredBuckets)
        {
            if (existingBucketNames.Contains(bucketName))
            {
                continue;
            }

            await _s3Client.PutBucketAsync(new PutBucketRequest
            {
                BucketName = bucketName
            });

            _logger.LogInformation("Storage bucket created: {BucketName}", bucketName);
        }

        await ApplyCorsConfigurationAsync();
    }

    public string GeneratePresignedUploadUrl(
        string bucketName,
        string objectKey,
        string contentType,
        int expiryInMinutes = 15)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = objectKey,
            ContentType = contentType,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.AddMinutes(expiryInMinutes)
        };

        return NormalizePresignedPublicUrl(_presignClient.GetPreSignedURL(request));
    }

    public string GeneratePresignedDownloadUrl(
        string bucketName,
        string objectKey,
        int expiryInMinutes = 60)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(expiryInMinutes)
        };

        return NormalizePresignedPublicUrl(_presignClient.GetPreSignedURL(request));
    }

    public async Task UploadFileAsync(
        string bucketName,
        string objectKey,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        await using var stream = new MemoryStream(content);
        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = objectKey,
            InputStream = stream,
            ContentType = contentType
        }, cancellationToken);

        _logger.LogInformation(
            "Storage file uploaded. BucketName: {BucketName}, ObjectKey: {ObjectKey}",
            bucketName,
            objectKey);
    }

    public async Task UploadFileAsync(
        string bucketName,
        string objectKey,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false
        }, cancellationToken);

        _logger.LogInformation(
            "Storage file uploaded. BucketName: {BucketName}, ObjectKey: {ObjectKey}",
            bucketName,
            objectKey);
    }

    public async Task DeleteFileAsync(string bucketName, string objectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        try
        {
            await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = bucketName,
                Key = objectKey
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Storage file delete failed. BucketName: {BucketName}, ObjectKey: {ObjectKey}",
                bucketName,
                objectKey);
        }
    }

    public void Dispose()
    {
        if (_ownsPresignClient)
        {
            _presignClient.Dispose();
        }
    }

    private async Task ApplyCorsConfigurationAsync()
    {
        var allowedOrigins = _settings.CorsAllowedOrigins
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (allowedOrigins.Count == 0)
        {
            return;
        }

        var configuration = new CORSConfiguration
        {
            Rules =
            [
                new CORSRule
                {
                    AllowedOrigins = allowedOrigins,
                    AllowedMethods = ["GET", "PUT", "HEAD"],
                    AllowedHeaders = ["*"],
                    ExposeHeaders = ["ETag"],
                    MaxAgeSeconds = 3000
                }
            ]
        };

        foreach (var bucketName in RequiredBuckets)
        {
            try
            {
                await _s3Client.PutCORSConfigurationAsync(new PutCORSConfigurationRequest
                {
                    BucketName = bucketName,
                    Configuration = configuration
                });
            }
            catch (AmazonS3Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Storage CORS configuration could not be applied. BucketName: {BucketName}, ErrorCode: {ErrorCode}, Message: {Message}",
                    bucketName,
                    exception.ErrorCode,
                    exception.Message);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Storage CORS configuration could not be applied. BucketName: {BucketName}, Message: {Message}",
                    bucketName,
                    exception.Message);
            }
        }
    }

    private static IAmazonS3 CreatePresignClient(
        StorageSettings settings,
        IAmazonS3 fallbackClient,
        out bool ownsClient)
    {
        if (string.IsNullOrWhiteSpace(settings.PublicServiceUrl) ||
            string.Equals(settings.PublicServiceUrl, settings.ServiceUrl, StringComparison.OrdinalIgnoreCase))
        {
            ownsClient = false;
            return fallbackClient;
        }

        var config = new AmazonS3Config
        {
            ServiceURL = settings.PublicServiceUrl,
            ForcePathStyle = settings.ForcePathStyle,
            UseHttp = settings.PublicServiceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        };

        ownsClient = true;
        return new AmazonS3Client(
            new BasicAWSCredentials(settings.AccessKey, settings.SecretKey),
            config);
    }

    private string NormalizePresignedPublicUrl(string presignedUrl)
    {
        if (!Uri.TryCreate(_settings.PublicServiceUrl, UriKind.Absolute, out var publicEndpoint) ||
            !Uri.TryCreate(presignedUrl, UriKind.Absolute, out var generatedUrl))
        {
            return presignedUrl;
        }

        var normalizedUrl = new UriBuilder(generatedUrl)
        {
            Scheme = publicEndpoint.Scheme,
            Host = publicEndpoint.Host,
            Port = publicEndpoint.IsDefaultPort ? -1 : publicEndpoint.Port
        };

        return normalizedUrl.Uri.AbsoluteUri;
    }
}
