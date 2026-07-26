using CraftoraApi.DTOs.Order;
using CraftoraApi.DTOs.Subscription;
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
            Cvv: "123"));

        Assert.Contains(invalid.Errors, error => error.PropertyName == nameof(DirectCheckoutRequestDto.ProductId));
        Assert.True(valid.IsValid);
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
}
