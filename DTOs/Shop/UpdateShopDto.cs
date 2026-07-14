using System.ComponentModel.DataAnnotations;
using CraftoraApi.DTOs.Validation;

namespace CraftoraApi.DTOs.Shop;

public sealed record UpdateShopDto(
    [property: StringLength(100, MinimumLength = 3, ErrorMessage = "Mağaza adı 3 ile 100 karakter arasında olmalıdır.")]
    string? ShopName,

    [property: StringLength(255, ErrorMessage = "Kısa açıklama en fazla 255 karakter olabilir.")]
    string? ShortDescription,

    [property: StringLength(4000, ErrorMessage = "Aciklama en fazla 4000 karakter olabilir.")]
    string? Description,

    [property: Url(ErrorMessage = "Geçerli bir web sitesi adresi giriniz.")]
    [property: StringLength(2048, ErrorMessage = "Web sitesi adresi en fazla 2048 karakter olabilir.")]
    string? ExternalUrl,

    [property: StringLength(4000, ErrorMessage = "Sosyal medya baglantilari en fazla 4000 karakter olabilir.")]
    [property: JsonObject]
    string? SocialLinks,

    [property: StringLength(1024, ErrorMessage = "Logo dosya anahtari en fazla 1024 karakter olabilir.")]
    string? LogoUrl,

    [property: StringLength(1024, ErrorMessage = "Banner dosya anahtari en fazla 1024 karakter olabilir.")]
    string? BannerUrl);
