namespace CraftoraApi.Services.Interfaces;

public interface IPaymentService
{
    Task<(bool IsSuccess, string TransactionId, string ErrorMessage)> ProcessPaymentAsync(
        decimal amount,
        string currency,
        string cardNumber);
}
