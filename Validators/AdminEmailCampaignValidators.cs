using CraftoraApi.DTOs.Admin;
using FluentValidation;

namespace CraftoraApi.Validators;

public sealed class AdminEmailCampaignPreviewRequestValidator
    : AbstractValidator<AdminEmailCampaignPreviewRequestDto>
{
    public AdminEmailCampaignPreviewRequestValidator()
    {
        RuleFor(item => item.Audience)
            .NotEmpty()
            .Must(AdminEmailCampaignValidation.IsSupportedAudience)
            .WithMessage("Hedef kitle all, users veya sellers olmalidir.");
        RuleFor(item => item.Subject)
            .NotEmpty()
            .MaximumLength(160);
        RuleFor(item => item.Message)
            .NotEmpty()
            .MaximumLength(10000);
    }
}

public sealed class AdminEmailCampaignSendRequestValidator
    : AbstractValidator<AdminEmailCampaignSendRequestDto>
{
    public AdminEmailCampaignSendRequestValidator()
    {
        RuleFor(item => item.Audience)
            .NotEmpty()
            .Must(AdminEmailCampaignValidation.IsSupportedAudience)
            .WithMessage("Hedef kitle all, users veya sellers olmalidir.");
        RuleFor(item => item.Subject)
            .NotEmpty()
            .MaximumLength(160);
        RuleFor(item => item.Message)
            .NotEmpty()
            .MaximumLength(10000);
        RuleFor(item => item.IdempotencyKey)
            .NotEmpty()
            .MaximumLength(100)
            .Matches("^[A-Za-z0-9._:-]+$")
            .WithMessage("Idempotency anahtari gecersiz.");
    }
}

internal static class AdminEmailCampaignValidation
{
    public static bool IsSupportedAudience(string audience)
    {
        return !string.IsNullOrWhiteSpace(audience) &&
            audience.Trim().ToLowerInvariant() is "all" or "users" or "sellers";
    }
}
