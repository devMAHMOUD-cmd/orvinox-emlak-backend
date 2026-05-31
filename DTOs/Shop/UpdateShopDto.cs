using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Shop;

public sealed record UpdateShopDto(
    [property: StringLength(100, MinimumLength = 3, ErrorMessage = "Mağaza adı 3 ile 100 karakter arasında olmalıdır.")]
    string? ShopName,

    [property: StringLength(255, ErrorMessage = "Kısa açıklama en fazla 255 karakter olabilir.")]
    string? ShortDescription,

    string? Description,

    [property: Url(ErrorMessage = "Geçerli bir web sitesi adresi giriniz.")]
    string? ExternalUrl,

    string? SocialLinks,

    string? LogoUrl,

    string? BannerUrl);
