using CraftoraApi.Services.Interfaces;

namespace CraftoraApi.Services;

public sealed class MockPaymentService : IPaymentService
{
    private static readonly Dictionary<string, (bool IsSuccess, string Error)> TestCards = new()
    {
        { "4000000000000002", (IsSuccess: false, Error: "Kart reddedildi - Yetersiz bakiye") },
        { "4000000000000069", (IsSuccess: false, Error: "Kart reddedildi - Hatali kod") },
        { "6011000000000405", (IsSuccess: false, Error: "Kart reddedildi - Suresi dolmus kart") },
        { "4111111111111111", (IsSuccess: true, Error: string.Empty) },
        { "5555555555554444", (IsSuccess: true, Error: string.Empty) },
        { "378282246310005", (IsSuccess: true, Error: string.Empty) }
    };

    public async Task<(bool IsSuccess, string TransactionId, string ErrorMessage)> ProcessPaymentAsync(
        decimal amount,
        string currency,
        string cardNumber)
    {
        await Task.Delay(1000);

        if (!TestCards.TryGetValue(cardNumber, out var testResult))
        {
            return (
                IsSuccess: false,
                TransactionId: string.Empty,
                ErrorMessage: "Gecersiz mock odeme karti.");
        }

        if (!testResult.IsSuccess)
        {
            return (
                IsSuccess: false,
                TransactionId: string.Empty,
                ErrorMessage: testResult.Error);
        }

        return (
            IsSuccess: true,
            TransactionId: $"txn_mock_{Guid.NewGuid():N}",
            ErrorMessage: string.Empty);
    }

    public async Task<(bool IsSuccess, string RefundId, string ErrorMessage)> RefundPaymentAsync(
        string providerTransactionId,
        decimal amount,
        string currency)
    {
        await Task.Delay(250);

        if (string.IsNullOrWhiteSpace(providerTransactionId) ||
            !providerTransactionId.StartsWith("txn_mock_", StringComparison.Ordinal) ||
            amount < 0 ||
            string.IsNullOrWhiteSpace(currency))
        {
            return (
                IsSuccess: false,
                RefundId: string.Empty,
                ErrorMessage: "Mock odeme iade istegi gecersiz.");
        }

        return (
            IsSuccess: true,
            RefundId: $"rfnd_mock_{Guid.NewGuid():N}",
            ErrorMessage: string.Empty);
    }
}
