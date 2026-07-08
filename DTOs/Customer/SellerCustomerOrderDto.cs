namespace CraftoraApi.DTOs.Customer;

public sealed record SellerCustomerOrderDto(
    Guid OrderId,
    string OrderNumber,
    string ProductTitle,
    decimal Amount,
    string Status,
    DateTime? CreatedAt);
