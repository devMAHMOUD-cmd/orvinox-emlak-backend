using System.Linq.Expressions;
using CraftoraApi.DTOs.Order;
using CraftoraApi.DTOs.Subscription;
using FluentValidation;

namespace CraftoraApi.Validators;

public sealed class CheckoutRequestDtoValidator : AbstractValidator<CheckoutRequestDto>
{
    public CheckoutRequestDtoValidator()
    {
        PaymentRequestValidationRules.Apply(
            this,
            request => request.CardNumber,
            request => request.Expiry,
            request => request.Cvv);
    }
}

public sealed class DirectCheckoutRequestDtoValidator : AbstractValidator<DirectCheckoutRequestDto>
{
    public DirectCheckoutRequestDtoValidator()
    {
        RuleFor(request => request.ProductId)
            .NotEmpty()
            .WithMessage("Urun zorunludur.");

        PaymentRequestValidationRules.Apply(
            this,
            request => request.CardNumber,
            request => request.Expiry,
            request => request.Cvv);
    }
}

public sealed class StartSubscriptionRequestDtoValidator : AbstractValidator<StartSubscriptionRequestDto>
{
    public StartSubscriptionRequestDtoValidator()
    {
        PaymentRequestValidationRules.Apply(
            this,
            request => request.CardNumber,
            request => request.Expiry,
            request => request.Cvv);
    }
}

internal static class PaymentRequestValidationRules
{
    internal static void Apply<T>(
        AbstractValidator<T> validator,
        Expression<Func<T, string>> cardNumber,
        Expression<Func<T, string>> expiry,
        Expression<Func<T, string>> cvv)
    {
        validator.RuleFor(cardNumber)
            .NotEmpty()
            .Matches(@"^\d{13,19}$")
            .WithMessage("Kart numarasi 13 ile 19 hane arasinda olmalidir.");

        validator.RuleFor(expiry)
            .NotEmpty()
            .Matches(@"^(0[1-9]|1[0-2])/\d{2}$")
            .Must(NotExpired)
            .WithMessage("Gecerli bir son kullanma tarihi MM/YY formatinda girilmelidir.");

        validator.RuleFor(cvv)
            .NotEmpty()
            .Matches(@"^\d{3,4}$")
            .WithMessage("CVV 3 veya 4 haneli olmalidir.");
    }

    private static bool NotExpired(string expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry) ||
            expiry.Length != 5 ||
            expiry[2] != '/' ||
            !int.TryParse(expiry[..2], out var month) ||
            !int.TryParse(expiry[3..], out var shortYear) ||
            month is < 1 or > 12)
        {
            return false;
        }

        var expiresAt = new DateTime(2000 + shortYear, month, 1).AddMonths(1);
        var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        return expiresAt > currentMonth;
    }
}
