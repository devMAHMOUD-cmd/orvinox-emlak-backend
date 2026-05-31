using CraftoraApi.Models.Elasticsearch;

namespace CraftoraApi.DTOs.Search;

public sealed record SearchResponseDto(
    long TotalCount,
    List<ProductDocument> Items);
