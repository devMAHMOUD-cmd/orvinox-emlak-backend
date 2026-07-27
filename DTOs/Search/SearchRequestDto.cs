using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Search;

public sealed record SearchRequestDto(
    [property: StringLength(200, ErrorMessage = "Arama metni en fazla 200 karakter olabilir.")]
    string? Query,

    Guid? CategoryId,

    [property: Range(
        typeof(decimal),
        "0",
        "99999999.99",
        ParseLimitsInInvariantCulture = true,
        ConvertValueInInvariantCulture = true,
        ErrorMessage = "Minimum fiyat geçersiz.")]
    decimal? MinPrice,

    [property: Range(
        typeof(decimal),
        "0",
        "99999999.99",
        ParseLimitsInInvariantCulture = true,
        ConvertValueInInvariantCulture = true,
        ErrorMessage = "Maksimum fiyat geçersiz.")]
    decimal? MaxPrice,

    [property: Range(1, int.MaxValue, ErrorMessage = "Sayfa numarası en az 1 olmalıdır.")]
    int Page = 1,

    [property: Range(1, 100, ErrorMessage = "Sayfa boyutu 1 ile 100 arasında olmalıdır.")]
    int PageSize = 10);
