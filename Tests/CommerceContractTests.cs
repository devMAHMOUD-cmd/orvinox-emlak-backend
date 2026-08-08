using CraftoraApi.DTOs.Order;
using CraftoraApi.DTOs.Subscription;
using CraftoraApi.DTOs.Shop;
using CraftoraApi.Services;
using CraftoraApi.Validators;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class CommerceContractTests
{
    [Fact]
    public void Checkout_rejects_invalid_card_fields()
    {
        var validator = new CheckoutRequestDtoValidator();
        var result = validator.Validate(new CheckoutRequestDto(
            CardNumber: "123",
            Expiry: "01/20",
            Cvv: "1"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CheckoutRequestDto.CardNumber));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CheckoutRequestDto.Expiry));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CheckoutRequestDto.Cvv));
    }

    [Fact]
    public void Direct_checkout_requires_product_and_accepts_valid_mock_card_shape()
    {
        var validator = new DirectCheckoutRequestDtoValidator();
        var invalid = validator.Validate(new DirectCheckoutRequestDto(
            ProductId: Guid.Empty,
            CardNumber: "4111111111111111",
            Expiry: "12/99",
            Cvv: "123"));
        var valid = validator.Validate(new DirectCheckoutRequestDto(
            ProductId: Guid.NewGuid(),
            CardNumber: "4111111111111111",
            Expiry: "12/99",
            Cvv: "123",
            CouponCode: "SAVE20"));

        Assert.Contains(invalid.Errors, error => error.PropertyName == nameof(DirectCheckoutRequestDto.ProductId));
        Assert.True(valid.IsValid);
    }

    [Fact]
    public void Cart_checkout_rejects_duplicate_coupon_assignments()
    {
        var productId = Guid.NewGuid();
        var validator = new CheckoutRequestDtoValidator();
        var result = validator.Validate(new CheckoutRequestDto(
            CardNumber: "4111111111111111",
            Expiry: "12/99",
            Cvv: "123",
            Coupons:
            [
                new CheckoutCouponDto(productId, "SAVE20"),
                new CheckoutCouponDto(productId, "OTHER20")
            ]));

        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(CheckoutRequestDto.Coupons));
    }

    [Fact]
    public void Direct_checkout_rejects_malformed_coupon_code()
    {
        var validator = new DirectCheckoutRequestDtoValidator();
        var result = validator.Validate(new DirectCheckoutRequestDto(
            ProductId: Guid.NewGuid(),
            CardNumber: "4111111111111111",
            Expiry: "12/99",
            Cvv: "123",
            CouponCode: "X"));

        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(DirectCheckoutRequestDto.CouponCode));
    }

    [Fact]
    public void Subscription_uses_same_expiry_validation()
    {
        var validator = new StartSubscriptionRequestDtoValidator();
        var result = validator.Validate(new StartSubscriptionRequestDto(
            CardNumber: "4111111111111111",
            Expiry: "01/20",
            Cvv: "123"));

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(StartSubscriptionRequestDto.Expiry));
    }

    [Fact]
    public void Shop_subscription_rejects_invalid_payment_before_provisioning()
    {
        var validator = new StartShopSubscriptionRequestDtoValidator();
        var request = new StartShopSubscriptionRequestDto(
            new CreateShopDto(
                "Test Magaza",
                null,
                null,
                null,
                null,
                null,
                null),
            new StartSubscriptionRequestDto(
                "4000000000000002",
                "01/20",
                "1"));

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName.Contains("Payment.Expiry"));
        Assert.Contains(result.Errors, error => error.PropertyName.Contains("Payment.Cvv"));
    }

    [Fact]
    public async Task Mock_payment_accepts_documented_success_card()
    {
        var service = new MockPaymentService();

        var result = await service.ProcessPaymentAsync(10m, "USD", "4111111111111111");

        Assert.True(result.IsSuccess);
        Assert.StartsWith("txn_mock_", result.TransactionId);
    }

    [Fact]
    public async Task Mock_payment_rejects_documented_failure_card()
    {
        var service = new MockPaymentService();

        var result = await service.ProcessPaymentAsync(10m, "USD", "4000000000000002");

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.ErrorMessage);
    }

    [Fact]
    public async Task Mock_payment_rejects_unknown_card()
    {
        var service = new MockPaymentService();

        var result = await service.ProcessPaymentAsync(10m, "USD", "4242424242424242");

        Assert.False(result.IsSuccess);
        Assert.Equal("Gecersiz mock odeme karti.", result.ErrorMessage);
    }

    [Fact]
    public async Task Mock_payment_refunds_a_valid_mock_transaction()
    {
        var service = new MockPaymentService();

        var result = await service.RefundPaymentAsync(
            "txn_mock_1234567890",
            40m,
            "USD");

        Assert.True(result.IsSuccess);
        Assert.StartsWith("rfnd_mock_", result.RefundId);
    }

    [Fact]
    public async Task Mock_payment_rejects_an_unknown_refund_transaction()
    {
        var service = new MockPaymentService();

        var result = await service.RefundPaymentAsync(
            "stripe_unknown",
            40m,
            "USD");

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.ErrorMessage);
    }
}
