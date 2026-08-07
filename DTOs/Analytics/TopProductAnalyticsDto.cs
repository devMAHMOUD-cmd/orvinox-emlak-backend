using CraftoraApi.DTOs.Common;

namespace CraftoraApi.DTOs.Analytics;

public sealed record TopProductAnalyticsDto(
    Guid ProductId,
    string Title,
    string ProductType,
    int Views,
    int Sales,
    decimal Revenue,
    IReadOnlyList<CurrencyAmountDto> RevenueByCurrency,
    double ViewToPurchaseRate);
