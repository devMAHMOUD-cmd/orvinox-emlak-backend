namespace CraftoraApi.DTOs.Analytics;

public sealed record SellerAnalyticsFunnelDto(
    DateTime StartDate,
    DateTime EndDate,
    IReadOnlyList<FunnelStepDto> Steps);

public sealed record FunnelStepDto(
    string Key,
    string Label,
    int Count,
    double DropOffRateFromPrevious);
