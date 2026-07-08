namespace CraftoraApi.DTOs.Search;

public sealed record GlobalSearchResponseDto(
    string Query,
    List<ProductSearchResultDto> Products,
    List<CourseSearchResultDto> Courses,
    List<MediaSearchResultDto> Media,
    List<ShopSearchResultDto> Shops);
