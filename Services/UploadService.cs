using System.Text.RegularExpressions;
using CraftoraApi.Data;
using CraftoraApi.DTOs;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed partial class UploadService : IUploadService
{
    private const string PublicBucket = "public-assets";
    private const string PrivateBucket = "private-products";
    private const long MaximumPublicImageBytes = 15L * 1024 * 1024;
    private const long MaximumPublicVideoBytes = 250L * 1024 * 1024;
    private const long MaximumPrivateFileBytes = 2L * 1024 * 1024 * 1024;

    private static readonly HashSet<string> PublicImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif"
    };

    private static readonly HashSet<string> PublicVideoContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "video/mp4",
        "video/quicktime",
        "video/webm"
    };

    private static readonly HashSet<string> BlockedPrivateContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/x-msdownload",
        "application/x-dosexec",
        "application/x-executable",
        "application/x-sh",
        "application/x-bat"
    };

    private readonly AppDbContext _dbContext;
    private readonly IStorageService _storageService;
    private readonly ILogger<UploadService> _logger;

    public UploadService(
        AppDbContext dbContext,
        IStorageService storageService,
        ILogger<UploadService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public PresignedUploadResponseDto GenerateUploadUrl(
        Guid userId,
        GeneratePresignedUrlDto dto,
        bool isPublic)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var contentType = NormalizeContentType(dto.ContentType);
        ValidateContentType(contentType, isPublic);

        var fileName = SanitizeFileName(dto.FileName);
        var visibility = isPublic ? "public" : "private";
        var bucketName = isPublic ? PublicBucket : PrivateBucket;
        var objectKey = $"users/{userId:D}/{visibility}/{Guid.NewGuid():N}_{fileName}";
        var uploadUrl = _storageService.GeneratePresignedUploadUrl(
            bucketName,
            objectKey,
            contentType,
            expiryInMinutes: 30);

        return new PresignedUploadResponseDto(uploadUrl, objectKey);
    }

    public async Task ValidateOwnedObjectAsync(
        Guid userId,
        string objectKey,
        bool isPublic,
        CancellationToken cancellationToken = default)
    {
        var normalizedObjectKey = NormalizeOwnedObjectKey(userId, objectKey);
        var expectedBucket = isPublic ? PublicBucket : PrivateBucket;
        var actualBucket = GetBucketForOwnedObjectKey(userId, normalizedObjectKey);
        if (!string.Equals(expectedBucket, actualBucket, StringComparison.Ordinal))
        {
            throw new BadRequestException("Dosya visibility ve bucket bilgileri uyusmuyor.");
        }

        var objectInfo = await _storageService.GetObjectInfoAsync(
            actualBucket,
            normalizedObjectKey,
            cancellationToken);
        if (objectInfo is null)
        {
            throw new NotFoundException("Yuklenen dosya storage uzerinde bulunamadi.");
        }

        ValidateCompletedObject(actualBucket, objectInfo);
    }

    public async Task ValidateMediaVideoAsync(
        Guid userId,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        var objectInfo = await GetOwnedObjectInfoAsync(
            userId,
            objectKey,
            PrivateBucket,
            cancellationToken);
        var contentType = NormalizeContentType(objectInfo.ContentType);

        if (!PublicVideoContentTypes.Contains(contentType))
        {
            throw new BadRequestException("Reels dosyasi desteklenen bir video turunde olmalidir.");
        }

        if (objectInfo.ContentLength <= 0 || objectInfo.ContentLength > MaximumPublicVideoBytes)
        {
            throw new BadRequestException("Reels video boyutu izin verilen sinirlar disinda.");
        }
    }

    public async Task ValidateMediaThumbnailAsync(
        Guid userId,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        var objectInfo = await GetOwnedObjectInfoAsync(
            userId,
            objectKey,
            PublicBucket,
            cancellationToken);
        var contentType = NormalizeContentType(objectInfo.ContentType);

        if (!PublicImageContentTypes.Contains(contentType))
        {
            throw new BadRequestException("Reels thumbnail dosyasi desteklenen bir gorsel turunde olmalidir.");
        }

        if (objectInfo.ContentLength <= 0 || objectInfo.ContentLength > MaximumPublicImageBytes)
        {
            throw new BadRequestException("Reels thumbnail boyutu izin verilen sinirlar disinda.");
        }
    }

    public async Task CompleteUploadAsync(
        Guid userId,
        UploadCompleteDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var objectKey = NormalizeOwnedObjectKey(userId, dto.ObjectKey);
        var bucketName = GetBucketForOwnedObjectKey(userId, objectKey);
        var objectInfo = await _storageService.GetObjectInfoAsync(
            bucketName,
            objectKey,
            cancellationToken);

        if (objectInfo is null)
        {
            throw new NotFoundException("Yuklenen dosya storage uzerinde bulunamadi.");
        }

        try
        {
            ValidateCompletedObject(bucketName, objectInfo);
            await VerifyEntityOwnershipAndReferenceAsync(
                userId,
                dto.EntityType,
                dto.EntityId,
                bucketName,
                objectKey,
                objectInfo.ContentType,
                cancellationToken);
        }
        catch (CraftoraException exception) when (exception.StatusCode is >= 400 and < 500)
        {
            await DeleteRejectedUploadAsync(bucketName, objectKey, cancellationToken);
            throw;
        }

        _logger.LogInformation(
            "Upload completed. UserId: {UserId}, BucketName: {BucketName}, ObjectKey: {ObjectKey}, EntityType: {EntityType}, EntityId: {EntityId}, Size: {Size}",
            userId,
            bucketName,
            objectKey,
            dto.EntityType,
            dto.EntityId,
            objectInfo.ContentLength);
    }

    public async Task DeleteOwnedFileAsync(
        Guid userId,
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedBucket = bucketName.Trim().ToLowerInvariant();
        if (normalizedBucket is not PublicBucket and not PrivateBucket)
        {
            throw new BadRequestException("Gecersiz storage bucket degeri.");
        }

        var normalizedObjectKey = NormalizeOwnedObjectKey(userId, objectKey);
        if (!string.Equals(
                normalizedBucket,
                GetBucketForOwnedObjectKey(userId, normalizedObjectKey),
                StringComparison.Ordinal))
        {
            throw new BadRequestException("Dosya bucket ve object key bilgileri uyusmuyor.");
        }

        if (await IsObjectReferencedAsync(normalizedObjectKey, cancellationToken))
        {
            throw new ConflictException("Kullanilan bir dosya silinemez. Once ilgili kaydi guncelleyin.");
        }

        await _storageService.DeleteFileAsync(
            normalizedBucket,
            normalizedObjectKey,
            cancellationToken);
    }

    private async Task VerifyEntityOwnershipAndReferenceAsync(
        Guid userId,
        string entityType,
        Guid entityId,
        string bucketName,
        string objectKey,
        string? contentType,
        CancellationToken cancellationToken)
    {
        switch (entityType.Trim().ToLowerInvariant())
        {
            case "shop":
                await VerifyShopReferenceAsync(
                    userId,
                    entityId,
                    bucketName,
                    objectKey,
                    cancellationToken);
                return;
            case "product":
                await VerifyProductReferenceAsync(
                    userId,
                    entityId,
                    bucketName,
                    objectKey,
                    contentType,
                    cancellationToken);
                return;
            case "course":
                await VerifyCourseReferenceAsync(
                    userId,
                    entityId,
                    bucketName,
                    objectKey,
                    contentType,
                    cancellationToken);
                return;
            case "media":
                await VerifyMediaReferenceAsync(
                    userId,
                    entityId,
                    bucketName,
                    objectKey,
                    contentType,
                    cancellationToken);
                return;
            default:
                throw new BadRequestException("Gecersiz upload entity type degeri.");
        }
    }

    private async Task VerifyShopReferenceAsync(
        Guid userId,
        Guid shopId,
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken)
    {
        if (bucketName != PublicBucket)
        {
            throw new BadRequestException("Magaza gorselleri public bucket icinde olmalidir.");
        }

        var shop = await _dbContext.Shops
            .AsNoTracking()
            .Where(item => item.Id == shopId && item.UserId == userId)
            .Select(item => new { item.LogoUrl, item.BannerUrl })
            .FirstOrDefaultAsync(cancellationToken);

        if (shop is null)
        {
            throw new NotFoundException("Magaza bulunamadi.");
        }

        if (!MatchesObjectKey(shop.LogoUrl, objectKey) &&
            !MatchesObjectKey(shop.BannerUrl, objectKey))
        {
            throw new BadRequestException("Dosya bu magazanin logo veya banner alanina bagli degil.");
        }
    }

    private async Task VerifyProductReferenceAsync(
        Guid userId,
        Guid productId,
        string bucketName,
        string objectKey,
        string? contentType,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({productId.ToString("D")}, 0));",
            cancellationToken);

        var product = await _dbContext.Products
            .Include(item => item.ProductImages)
            .FirstOrDefaultAsync(
                item => item.Id == productId && item.Shop.UserId == userId,
                cancellationToken);

        if (product is null)
        {
            throw new NotFoundException("Urun bulunamadi.");
        }

        if (bucketName == PrivateBucket)
        {
            if (!MatchesObjectKey(product.FileUrl, objectKey))
            {
                throw new BadRequestException("Dosya bu urunun indirilebilir dosya alanina bagli degil.");
            }

            await transaction.CommitAsync(cancellationToken);
            return;
        }

        if (IsImageContentType(contentType))
        {
            if (product.ProductImages.All(image => image.ObjectKey != objectKey))
            {
                if (product.ProductImages.Count >= 8)
                {
                    throw new BadRequestException("Bir urune en fazla 8 gorsel eklenebilir.");
                }

                product.ProductImages.Add(new ProductImage
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    ObjectKey = objectKey,
                    SortOrder = product.ProductImages.Count == 0
                        ? 0
                        : product.ProductImages.Max(image => image.SortOrder) + 1,
                    CreatedAt = DateTime.UtcNow
                });
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return;
        }

        if (!MatchesObjectKey(product.PreviewVideoUrl, objectKey))
        {
            throw new BadRequestException("Dosya bu urunun onizleme videosuna bagli degil.");
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task VerifyCourseReferenceAsync(
        Guid userId,
        Guid courseId,
        string bucketName,
        string objectKey,
        string? contentType,
        CancellationToken cancellationToken)
    {
        var course = await _dbContext.Courses
            .Include(item => item.Product)
                .ThenInclude(product => product.ProductImages)
            .Include(item => item.CourseSections)
                .ThenInclude(section => section.CourseLessons)
                    .ThenInclude(lesson => lesson.LessonResources)
            .FirstOrDefaultAsync(
                item => item.Id == courseId && item.Product.Shop.UserId == userId,
                cancellationToken);

        if (course is null)
        {
            throw new NotFoundException("Kurs bulunamadi.");
        }

        if (bucketName == PublicBucket)
        {
            if (IsImageContentType(contentType) &&
                MatchesObjectKey(course.Product.CoverImageUrl, objectKey))
            {
                if (course.Product.ProductImages.All(image => image.ObjectKey != objectKey))
                {
                    course.Product.ProductImages.Add(new ProductImage
                    {
                        Id = Guid.NewGuid(),
                        ProductId = course.ProductId,
                        ObjectKey = objectKey,
                        SortOrder = 0,
                        CreatedAt = DateTime.UtcNow
                    });
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                return;
            }

            if (MatchesObjectKey(course.Product.PreviewVideoUrl, objectKey))
            {
                return;
            }

            throw new BadRequestException("Dosya bu kursun kapak veya onizleme alanina bagli degil.");
        }

        var isLessonFile = course.CourseSections
            .SelectMany(section => section.CourseLessons)
            .Any(lesson =>
                MatchesObjectKey(lesson.VideoUrl, objectKey) ||
                lesson.LessonResources.Any(resource =>
                    MatchesObjectKey(resource.FileUrl, objectKey)));

        if (!isLessonFile)
        {
            throw new BadRequestException("Dosya bu kursun ders veya kaynak alanina bagli degil.");
        }
    }

    private async Task VerifyMediaReferenceAsync(
        Guid userId,
        Guid mediaId,
        string bucketName,
        string objectKey,
        string? contentType,
        CancellationToken cancellationToken)
    {
        var media = await _dbContext.Media
            .AsNoTracking()
            .Where(item => item.Id == mediaId && item.Shop.UserId == userId)
            .Select(item => new { item.VideoUrl, item.ThumbnailUrl })
            .FirstOrDefaultAsync(cancellationToken);

        if (media is null)
        {
            throw new NotFoundException("Medya bulunamadi.");
        }

        var isValidReference = bucketName == PrivateBucket
            ? MatchesObjectKey(media.VideoUrl, objectKey)
            : MatchesObjectKey(media.ThumbnailUrl, objectKey);

        if (!isValidReference)
        {
            throw new BadRequestException("Dosya bu medya kaydina bagli degil.");
        }

        var normalizedContentType = NormalizeContentType(contentType);
        if (bucketName == PrivateBucket && !PublicVideoContentTypes.Contains(normalizedContentType))
        {
            throw new BadRequestException("Reels dosyasi desteklenen bir video turunde olmalidir.");
        }

        if (bucketName == PublicBucket && !PublicImageContentTypes.Contains(normalizedContentType))
        {
            throw new BadRequestException("Reels thumbnail dosyasi desteklenen bir gorsel turunde olmalidir.");
        }
    }

    private async Task<StorageObjectInfo> GetOwnedObjectInfoAsync(
        Guid userId,
        string objectKey,
        string expectedBucket,
        CancellationToken cancellationToken)
    {
        var normalizedObjectKey = NormalizeOwnedObjectKey(userId, objectKey);
        var actualBucket = GetBucketForOwnedObjectKey(userId, normalizedObjectKey);
        if (!string.Equals(expectedBucket, actualBucket, StringComparison.Ordinal))
        {
            throw new BadRequestException("Dosya visibility ve bucket bilgileri uyusmuyor.");
        }

        var objectInfo = await _storageService.GetObjectInfoAsync(
            actualBucket,
            normalizedObjectKey,
            cancellationToken);
        return objectInfo
            ?? throw new NotFoundException("Yuklenen dosya storage uzerinde bulunamadi.");
    }

    private async Task<bool> IsObjectReferencedAsync(
        string objectKey,
        CancellationToken cancellationToken)
    {
        if (await _dbContext.Shops.AnyAsync(
                item =>
                    item.LogoUrl == objectKey ||
                    (item.LogoUrl != null && item.LogoUrl.EndsWith("/" + objectKey)) ||
                    item.BannerUrl == objectKey ||
                    (item.BannerUrl != null && item.BannerUrl.EndsWith("/" + objectKey)),
                cancellationToken) ||
            await _dbContext.Products.AnyAsync(
                item =>
                    item.CoverImageUrl == objectKey ||
                    (item.CoverImageUrl != null && item.CoverImageUrl.EndsWith("/" + objectKey)) ||
                    item.PreviewVideoUrl == objectKey ||
                    (item.PreviewVideoUrl != null && item.PreviewVideoUrl.EndsWith("/" + objectKey)) ||
                    item.FileUrl == objectKey ||
                    (item.FileUrl != null && item.FileUrl.EndsWith("/" + objectKey)),
                cancellationToken) ||
            await _dbContext.ProductImages.AnyAsync(
                item =>
                    item.ObjectKey == objectKey ||
                    item.ObjectKey.EndsWith("/" + objectKey),
                cancellationToken) ||
            await _dbContext.Media.AnyAsync(
                item =>
                    item.VideoUrl == objectKey ||
                    item.VideoUrl.EndsWith("/" + objectKey) ||
                    item.ThumbnailUrl == objectKey ||
                    (item.ThumbnailUrl != null && item.ThumbnailUrl.EndsWith("/" + objectKey)),
                cancellationToken) ||
            await _dbContext.CourseLessons.AnyAsync(
                item =>
                    item.VideoUrl == objectKey ||
                    (item.VideoUrl != null && item.VideoUrl.EndsWith("/" + objectKey)),
                cancellationToken) ||
            await _dbContext.LessonResources.AnyAsync(
                item =>
                    item.FileUrl == objectKey ||
                    item.FileUrl.EndsWith("/" + objectKey),
                cancellationToken))
        {
            return true;
        }

        return await _dbContext.Users.AnyAsync(
            item =>
                item.AvatarUrl == objectKey ||
                (item.AvatarUrl != null && item.AvatarUrl.EndsWith("/" + objectKey)),
            cancellationToken);
    }

    private async Task DeleteRejectedUploadAsync(
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await _storageService.DeleteFileAsync(bucketName, objectKey, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Rejected upload could not be deleted. BucketName: {BucketName}, ObjectKey: {ObjectKey}",
                bucketName,
                objectKey);
        }
    }

    private static void ValidateCompletedObject(
        string bucketName,
        StorageObjectInfo objectInfo)
    {
        var contentType = NormalizeContentType(objectInfo.ContentType);
        if (bucketName == PublicBucket)
        {
            ValidateContentType(contentType, isPublic: true);
            var maximumBytes = IsImageContentType(contentType)
                ? MaximumPublicImageBytes
                : MaximumPublicVideoBytes;

            if (objectInfo.ContentLength <= 0 || objectInfo.ContentLength > maximumBytes)
            {
                throw new BadRequestException("Public dosya boyutu izin verilen sinirlar disinda.");
            }

            return;
        }

        ValidateContentType(contentType, isPublic: false);
        if (objectInfo.ContentLength <= 0 || objectInfo.ContentLength > MaximumPrivateFileBytes)
        {
            throw new BadRequestException("Private dosya boyutu izin verilen sinirlar disinda.");
        }
    }

    private static void ValidateContentType(string contentType, bool isPublic)
    {
        if (isPublic)
        {
            if (!PublicImageContentTypes.Contains(contentType) &&
                !PublicVideoContentTypes.Contains(contentType))
            {
                throw new BadRequestException("Public upload icin desteklenmeyen dosya turu.");
            }

            return;
        }

        var supportedPrivateType =
            contentType.StartsWith("application/", StringComparison.OrdinalIgnoreCase) ||
            contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
            contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ||
            contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
            contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);

        if (!supportedPrivateType || BlockedPrivateContentTypes.Contains(contentType))
        {
            throw new BadRequestException("Private upload icin desteklenmeyen dosya turu.");
        }
    }

    private static string NormalizeOwnedObjectKey(Guid userId, string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new BadRequestException("Object key zorunludur.");
        }

        var normalized = objectKey.Trim().TrimStart('/');
        var expectedPrefix = $"users/{userId:D}/";
        if (!normalized.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Bu dosya mevcut kullaniciya ait degil.");
        }

        return normalized;
    }

    private static string GetBucketForOwnedObjectKey(Guid userId, string objectKey)
    {
        var publicPrefix = $"users/{userId:D}/public/";
        if (objectKey.StartsWith(publicPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return PublicBucket;
        }

        var privatePrefix = $"users/{userId:D}/private/";
        if (objectKey.StartsWith(privatePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return PrivateBucket;
        }

        throw new BadRequestException("Object key visibility bilgisi gecersiz.");
    }

    private static bool MatchesObjectKey(string? storedValue, string objectKey)
    {
        if (string.IsNullOrWhiteSpace(storedValue))
        {
            return false;
        }

        if (!Uri.TryCreate(storedValue, UriKind.Absolute, out var uri))
        {
            return string.Equals(
                storedValue.Trim().TrimStart('/'),
                objectKey,
                StringComparison.Ordinal);
        }

        return Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/').EndsWith(
            objectKey,
            StringComparison.Ordinal);
    }

    private static bool IsImageContentType(string? contentType)
    {
        return !string.IsNullOrWhiteSpace(contentType) &&
            contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new BadRequestException("Icerik tipi zorunludur.");
        }

        return contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
    }

    private static string SanitizeFileName(string fileName)
    {
        var baseName = Path.GetFileName(fileName?.Trim());
        if (string.IsNullOrWhiteSpace(baseName))
        {
            throw new BadRequestException("Gecersiz dosya adi.");
        }

        var sanitized = UnsafeFileNameCharactersRegex().Replace(baseName, "_").Trim(' ', '.', '_');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            throw new BadRequestException("Gecersiz dosya adi.");
        }

        return sanitized.Length <= 180 ? sanitized : sanitized[..180];
    }

    [GeneratedRegex(@"[^a-zA-Z0-9._-]+", RegexOptions.CultureInvariant)]
    private static partial Regex UnsafeFileNameCharactersRegex();
}
