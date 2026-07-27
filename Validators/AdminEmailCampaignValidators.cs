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
            .WithMessage("Hedef kitle all, users, sellers veya selected olmalidir.");
        RuleFor(item => item.Subject)
            .NotEmpty()
            .MaximumLength(160);
        RuleFor(item => item.Message)
            .NotEmpty()
            .MaximumLength(10000);
        RuleFor(item => item.UserIds)
            .Must(AdminEmailCampaignValidation.HasValidSelection)
            .WithMessage("Selected hedef kitlesi icin 1 ile 1000 arasinda kullanici secilmelidir.");
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
            .WithMessage("Hedef kitle all, users, sellers veya selected olmalidir.");
        RuleFor(item => item.Subject)
            .NotEmpty()
            .MaximumLength(160);
        RuleFor(item => item.Message)
            .NotEmpty()
            .MaximumLength(10000);
        RuleFor(item => item.UserIds)
            .Must(AdminEmailCampaignValidation.HasValidSelection)
            .WithMessage("Selected hedef kitlesi icin 1 ile 1000 arasinda kullanici secilmelidir.");
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
            audience.Trim().ToLowerInvariant() is "all" or "users" or "sellers" or "selected";
    }

    public static bool HasValidSelection<T>(T request, IReadOnlyList<Guid>? userIds)
        where T : class
    {
        var audience = request switch
        {
            AdminEmailCampaignPreviewRequestDto preview => preview.Audience,
            AdminEmailCampaignSendRequestDto send => send.Audience,
            _ => string.Empty
        };
        var selected = string.Equals(
            audience?.Trim(),
            "selected",
            StringComparison.OrdinalIgnoreCase);
        return !selected ||
            userIds is { Count: >= 1 and <= 1000 } &&
            userIds.All(id => id != Guid.Empty) &&
            userIds.Distinct().Count() == userIds.Count;
    }
}
