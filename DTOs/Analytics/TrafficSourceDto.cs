namespace CraftoraApi.DTOs.Analytics;

public sealed record TrafficSourceDto(
    string Source,
    int Visits,
    double Percentage);
