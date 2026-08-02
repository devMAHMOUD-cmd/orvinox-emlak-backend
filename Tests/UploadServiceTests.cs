using CraftoraApi.Data;
using CraftoraApi.DTOs;
using CraftoraApi.Middleware;
using CraftoraApi.Services;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class UploadServiceTests
{
    [Fact]
    public void Public_upload_uses_user_scoped_sanitized_object_key()
    {
        var userId = Guid.NewGuid();
        var storage = new FakeStorageService();
        using var dbContext = CreateDbContext();
        var service = new UploadService(
            dbContext,
            storage,
            NullLogger<UploadService>.Instance);

        var response = service.GenerateUploadUrl(
            userId,
            new GeneratePresignedUrlDto("../Kapak görseli 1.jpg", "image/jpeg"),
            isPublic: true);

        Assert.StartsWith($"users/{userId:D}/public/", response.ObjectKey);
        Assert.EndsWith("_Kapak_g_rseli_1.jpg", response.ObjectKey);
        Assert.Equal("public-assets", storage.LastBucketName);
        Assert.Equal("image/jpeg", storage.LastContentType);
    }

    [Fact]
    public void Upload_rejects_unsupported_or_executable_content_types()
    {
        using var dbContext = CreateDbContext();
        var service = new UploadService(
            dbContext,
            new FakeStorageService(),
            NullLogger<UploadService>.Instance);

        Assert.Throws<BadRequestException>(() =>
            service.GenerateUploadUrl(
                Guid.NewGuid(),
                new GeneratePresignedUrlDto("payload.exe", "application/x-msdownload"),
                isPublic: false));
        Assert.Throws<BadRequestException>(() =>
            service.GenerateUploadUrl(
                Guid.NewGuid(),
                new GeneratePresignedUrlDto("document.pdf", "application/pdf"),
                isPublic: true));
    }

    [Fact]
    public async Task Owned_object_validation_checks_bucket_metadata_and_size()
    {
        var userId = Guid.NewGuid();
        var storage = new FakeStorageService
        {
            NextObjectInfo = new StorageObjectInfo(
                ContentLength: 1024,
                ContentType: "image/jpeg",
                ETag: "etag",
                LastModified: DateTime.UtcNow)
        };
        using var dbContext = CreateDbContext();
        var service = new UploadService(
            dbContext,
            storage,
            NullLogger<UploadService>.Instance);

        await service.ValidateOwnedObjectAsync(
            userId,
            $"users/{userId:D}/public/image.jpg",
            isPublic: true);

        Assert.Equal("public-assets", storage.LastBucketName);
        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.ValidateOwnedObjectAsync(
                userId,
                $"users/{userId:D}/private/file.zip",
                isPublic: true));
    }

    [Fact]
    public async Task Media_video_requires_owned_private_video_object()
    {
        var userId = Guid.NewGuid();
        var storage = new FakeStorageService
        {
            NextObjectInfo = new StorageObjectInfo(
                ContentLength: 1024,
                ContentType: "video/mp4",
                ETag: "etag",
                LastModified: DateTime.UtcNow)
        };
        using var dbContext = CreateDbContext();
        var service = new UploadService(
            dbContext,
            storage,
            NullLogger<UploadService>.Instance);

        await service.ValidateMediaVideoAsync(
            userId,
            $"users/{userId:D}/private/reel.mp4");

        Assert.Equal("private-products", storage.LastBucketName);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.ValidateMediaVideoAsync(
                userId,
                $"users/{userId:D}/public/reel.mp4"));
    }

    [Fact]
    public async Task Media_video_rejects_non_video_private_object()
    {
        var userId = Guid.NewGuid();
        var storage = new FakeStorageService
        {
            NextObjectInfo = new StorageObjectInfo(
                ContentLength: 1024,
                ContentType: "application/pdf",
                ETag: "etag",
                LastModified: DateTime.UtcNow)
        };
        using var dbContext = CreateDbContext();
        var service = new UploadService(
            dbContext,
            storage,
            NullLogger<UploadService>.Instance);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.ValidateMediaVideoAsync(
                userId,
                $"users/{userId:D}/private/not-video.pdf"));
    }

    [Fact]
    public async Task Media_thumbnail_requires_owned_public_image_object()
    {
        var userId = Guid.NewGuid();
        var storage = new FakeStorageService
        {
            NextObjectInfo = new StorageObjectInfo(
                ContentLength: 1024,
                ContentType: "image/webp",
                ETag: "etag",
                LastModified: DateTime.UtcNow)
        };
        using var dbContext = CreateDbContext();
        var service = new UploadService(
            dbContext,
            storage,
            NullLogger<UploadService>.Instance);

        await service.ValidateMediaThumbnailAsync(
            userId,
            $"users/{userId:D}/public/reel.webp");

        Assert.Equal("public-assets", storage.LastBucketName);
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Options);
    }

    private sealed class FakeStorageService : IStorageService
    {
        public string? LastBucketName { get; private set; }
        public string? LastContentType { get; private set; }
        public StorageObjectInfo? NextObjectInfo { get; init; }

        public Task InitializeBucketsAsync() => Task.CompletedTask;

        public string GeneratePresignedUploadUrl(
            string bucketName,
            string objectKey,
            string contentType,
            int expiryInMinutes = 15)
        {
            LastBucketName = bucketName;
            LastContentType = contentType;
            return $"https://storage.test/{bucketName}/{objectKey}";
        }

        public string GeneratePresignedDownloadUrl(
            string bucketName,
            string objectKey,
            int expiryInMinutes = 60) =>
            $"https://storage.test/{bucketName}/{objectKey}";

        public Task UploadFileAsync(
            string bucketName,
            string objectKey,
            byte[] content,
            string contentType,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UploadFileAsync(
            string bucketName,
            string objectKey,
            Stream content,
            string contentType,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<StorageObjectInfo?> GetObjectInfoAsync(
            string bucketName,
            string objectKey,
            CancellationToken cancellationToken = default)
        {
            LastBucketName = bucketName;
            return Task.FromResult(NextObjectInfo);
        }

        public Task DownloadFileAsync(
            string bucketName,
            string objectKey,
            string destinationPath,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteFileAsync(
            string bucketName,
            string objectKey,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
