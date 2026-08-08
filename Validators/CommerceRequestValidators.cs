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

        RuleForEach(request => request.Coupons)
            .SetValidator(new CheckoutCouponDtoValidator());

        RuleFor(request => request.Coupons)
            .Must(coupons => coupons is null ||
                coupons.Select(coupon => coupon.ProductId).Distinct().Count() == coupons.Count)
            .WithMessage("Ayni urun icin birden fazla kupon gonderilemez.");
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

        RuleFor(request => request.CouponCode)
            .Must(code => string.IsNullOrWhiteSpace(code) ||
                code.Trim().Length is >= 2 and <= 50)
            .WithMessage("Kupon kodu 2 ile 50 karakter arasinda olmalidir.");
    }
}

public sealed class CheckoutCouponDtoValidator : AbstractValidator<CheckoutCouponDto>
{
    public CheckoutCouponDtoValidator()
    {
        RuleFor(coupon => coupon.ProductId)
            .NotEmpty()
            .WithMessage("Kupon urunu zorunludur.");

        RuleFor(coupon => coupon.Code)
            .NotEmpty()
            .Length(2, 50)
            .WithMessage("Kupon kodu 2 ile 50 karakter arasinda olmalidir.");
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

public sealed class StartShopSubscriptionRequestDtoValidator : AbstractValidator<StartShopSubscriptionRequestDto>
{
    public StartShopSubscriptionRequestDtoValidator()
    {
        RuleFor(request => request.Shop)
            .NotNull()
            .WithMessage("Magaza bilgileri zorunludur.");

        RuleFor(request => request.Payment)
            .NotNull()
            .SetValidator(new StartSubscriptionRequestDtoValidator());
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
