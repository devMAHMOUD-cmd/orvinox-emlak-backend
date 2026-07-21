namespace CraftoraApi.DTOs.Seller;

public sealed record WeeklySellerReportData(
    Guid ShopId,
    string ShopName,
    string SellerEmail,
    string SellerName,
    DateTime StartDate,
    DateTime EndDate,
    int UniqueVisitors,
    int ShopVisits,
    int ProductViews,
    int CourseViews,
    int MediaViews,
    int NewLikes,
    int NewComments,
    int CompletedSales,
    decimal TotalRevenue,
    IReadOnlyList<WeeklyReportProductDto> TopProducts,
    IReadOnlyList<WeeklyReportDailyPointDto> DailyPoints);

public sealed record WeeklyReportProductDto(
    Guid ProductId,
    string Title,
    int Views,
    int Sales,
    decimal Revenue);

public sealed record WeeklyReportDailyPointDto(
    string Date,
    int Views,
    int Sales,
    decimal Revenue);
