namespace CraftoraApi.DTOs.Shop;

public sealed record ShopTrafficReportDto(
    int TotalVisits,
    int UniqueVisitors,
    List<DailyVisitDto> DailyVisits);

public sealed record DailyVisitDto(
    DateTime Date,
    int Count);
