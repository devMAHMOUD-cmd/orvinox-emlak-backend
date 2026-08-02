namespace CraftoraApi.Services.Interfaces;

public sealed record StorageObjectInfo(
    long ContentLength,
    string? ContentType,
    string? ETag,
    DateTime? LastModified);

public interface IStorageService
{
    Task InitializeBucketsAsync();

    string GeneratePresignedUploadUrl(
        string bucketName,
        string objectKey,
        string contentType,
        int expiryInMinutes = 15);

    string GeneratePresignedDownloadUrl(
        string bucketName,
        string objectKey,
        int expiryInMinutes = 60);

    Task UploadFileAsync(
        string bucketName,
        string objectKey,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken = default);

    Task UploadFileAsync(
        string bucketName,
        string objectKey,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<StorageObjectInfo?> GetObjectInfoAsync(
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken = default);

    Task DownloadFileAsync(
        string bucketName,
        string objectKey,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task DeleteFileAsync(
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken = default);
}
