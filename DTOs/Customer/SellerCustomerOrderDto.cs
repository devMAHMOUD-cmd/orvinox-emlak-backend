namespace CraftoraApi.DTOs.Customer;

public sealed record SellerCustomerOrderDto(
    Guid OrderId,
    string OrderNumber,
    string ProductTitle,
    decimal Amount,
    string Currency,
    string Status,
    DateTime? CreatedAt);
