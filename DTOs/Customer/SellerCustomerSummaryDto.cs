using CraftoraApi.DTOs.Common;

namespace CraftoraApi.DTOs.Customer;

public sealed record SellerCustomerSummaryDto(
    int TotalCustomers,
    int Buyers,
    int Subscribers,
    int Visitors,
    int ReturningCustomers,
    decimal AverageCustomerValue,
    IReadOnlyList<CurrencyAmountDto> AverageCustomerValueByCurrency);
