namespace CraftoraApi.DTOs.Analytics;

public sealed record SellerAnalyticsTimeseriesDto(
    DateTime StartDate,
    DateTime EndDate,
    string Granularity,
    IReadOnlyList<SellerAnalyticsTimeseriesPointDto> Points);

public sealed record SellerAnalyticsTimeseriesPointDto(
    string Date,
    int ShopVisits,
    int ProductViews,
    int CourseViews,
    int MediaViews,
    int TotalViews,
    int AddToCartCount,
    int CheckoutStartedCount,
    int PurchaseCompletedCount,
    decimal Revenue,
    int UniqueVisitors);
