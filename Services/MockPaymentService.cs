using CraftoraApi.Services.Interfaces;

namespace CraftoraApi.Services;

public sealed class MockPaymentService : IPaymentService
{
    // Test kartları
    private static readonly Dictionary<string, (bool IsSuccess, string Error)> TestCards = new()
    {
        // Başarısız ödeme test kartları
        { "4000000000000002", (IsSuccess: false, Error: "Kart reddedildi - Yetersiz bakiye") },
        { "4000000000000069", (IsSuccess: false, Error: "Kart reddedildi - Hatalı kod") },
        { "6011000000000405", (IsSuccess: false, Error: "Kart reddedildi - Süresi dolmuş kart") },
        
        // Başarılı ödeme test kartları
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

        // Test kartı kontrolü
        if (TestCards.TryGetValue(cardNumber, out var testResult))
        {
            if (!testResult.IsSuccess)
            {
                return (
                    IsSuccess: false,
                    TransactionId: string.Empty,
                    ErrorMessage: testResult.Error);
            }
        }

        // Gerçek Stripe kullanıldığında, kartları buradan kontrol edilebilir
        return (
            IsSuccess: true,
            TransactionId: $"txn_mock_{Guid.NewGuid():N}",
            ErrorMessage: string.Empty);
    }
}
