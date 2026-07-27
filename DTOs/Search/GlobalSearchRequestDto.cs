using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Search;

public sealed record GlobalSearchRequestDto(
    [property: StringLength(200, ErrorMessage = "Arama metni en fazla 200 karakter olabilir.")]
    string? Query,

    [property: Range(1, int.MaxValue, ErrorMessage = "Sayfa numarası en az 1 olmalıdır.")]
    int Page = 1,

    [property: Range(1, 50, ErrorMessage = "Sayfa boyutu 1 ile 50 arasında olmalıdır.")]
    int PageSize = 10);
