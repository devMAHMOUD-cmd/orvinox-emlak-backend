namespace CraftoraApi.DTOs.Analytics;

public sealed record SellerAnalyticsOverviewDto(
    Guid ShopId,
    DateTime StartDate,
    DateTime EndDate,
    int TotalProductViews,
    int TotalCourseViews,
    int TotalShopVisits,
    int TotalMediaViews,
    int TotalDiscoveryViews,
    int AddToCartCount,
    int CheckoutStartedCount,
    int PurchaseCompletedCount,
    decimal TotalRevenue,
    int UniqueVisitors,
    int UniqueCustomers,
    double PurchaseConversionRate,
    double AverageCourseCompletionRate);
