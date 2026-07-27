using CraftoraApi.DTOs;
using FluentValidation;

namespace CraftoraApi.Validators;

public sealed class GeneratePresignedUrlDtoValidator
    : AbstractValidator<GeneratePresignedUrlDto>
{
    public GeneratePresignedUrlDtoValidator()
    {
        RuleFor(request => request.FileName)
            .NotEmpty()
            .MaximumLength(255);

        RuleFor(request => request.ContentType)
            .NotEmpty()
            .MaximumLength(100);
    }
}

public sealed class UploadCompleteDtoValidator : AbstractValidator<UploadCompleteDto>
{
    public UploadCompleteDtoValidator()
    {
        RuleFor(request => request.ObjectKey)
            .NotEmpty()
            .MaximumLength(1024);

        RuleFor(request => request.EntityType)
            .NotEmpty()
            .MaximumLength(30);

        RuleFor(request => request.EntityId)
            .NotEmpty();
    }
}
