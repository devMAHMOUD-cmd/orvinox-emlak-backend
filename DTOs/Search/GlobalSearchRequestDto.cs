namespace CraftoraApi.DTOs.Search;

public sealed record GlobalSearchRequestDto(
    string? Query,
    int Page = 1,
    int PageSize = 10);
