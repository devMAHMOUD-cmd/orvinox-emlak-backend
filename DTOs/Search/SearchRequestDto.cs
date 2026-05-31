namespace CraftoraApi.DTOs.Search;

public sealed record SearchRequestDto(
    string? Query,
    Guid? CategoryId,
    decimal? MinPrice,
    decimal? MaxPrice,
    int Page = 1,
    int PageSize = 10);
