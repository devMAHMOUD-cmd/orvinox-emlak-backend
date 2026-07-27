using CraftoraApi.DTOs;
using CraftoraApi.Validators;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class UploadRequestValidatorTests
{
    [Fact]
    public async Task Presign_rejects_oversized_file_name_and_content_type()
    {
        var request = new GeneratePresignedUrlDto(
            FileName: $"{new string('x', 252)}.png",
            ContentType: new string('x', 101));

        var result = await new GeneratePresignedUrlDtoValidator()
            .ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(GeneratePresignedUrlDto.FileName));
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(GeneratePresignedUrlDto.ContentType));
    }

    [Fact]
    public async Task Complete_rejects_oversized_fields_and_empty_entity_id()
    {
        var request = new UploadCompleteDto(
            ObjectKey: new string('x', 1025),
            EntityType: new string('x', 31),
            EntityId: Guid.Empty);

        var result = await new UploadCompleteDtoValidator()
            .ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(UploadCompleteDto.ObjectKey));
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(UploadCompleteDto.EntityType));
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(UploadCompleteDto.EntityId));
    }
}
