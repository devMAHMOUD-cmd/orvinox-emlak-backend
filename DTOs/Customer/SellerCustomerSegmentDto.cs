namespace CraftoraApi.DTOs.Customer;

public sealed record SellerCustomerSegmentDto(
    string Label,
    int Count,
    double Percentage);
