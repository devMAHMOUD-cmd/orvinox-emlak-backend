using CraftoraApi.DTOs;

namespace CraftoraApi.Services.Interfaces;

public interface IUploadService
{
    PresignedUploadResponseDto GenerateUploadUrl(
        Guid userId,
        GeneratePresignedUrlDto dto,
        bool isPublic);

    Task ValidateOwnedObjectAsync(
        Guid userId,
        string objectKey,
        bool isPublic,
        CancellationToken cancellationToken = default);

    Task ValidateMediaVideoAsync(
        Guid userId,
        string objectKey,
        CancellationToken cancellationToken = default);

    Task ValidateMediaThumbnailAsync(
        Guid userId,
        string objectKey,
        CancellationToken cancellationToken = default);

    Task ValidatePublicImageAsync(
        Guid userId,
        string objectKey,
        CancellationToken cancellationToken = default);

    Task CompleteUploadAsync(
        Guid userId,
        UploadCompleteDto dto,
        CancellationToken cancellationToken = default);

    Task DeleteOwnedFileAsync(
        Guid userId,
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken = default);
}
