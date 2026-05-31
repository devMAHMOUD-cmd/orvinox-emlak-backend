namespace CraftoraApi.Services.Interfaces;

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

    Task DeleteFileAsync(string bucketName, string objectKey);
}
