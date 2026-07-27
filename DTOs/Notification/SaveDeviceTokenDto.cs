using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Notification;

public sealed record SaveDeviceTokenDto(
    [property: Required(ErrorMessage = "Cihaz token bilgisi zorunludur.")]
    [property: StringLength(4096, MinimumLength = 16, ErrorMessage = "Cihaz token bilgisi 16 ile 4096 karakter arasında olmalıdır.")]
    string DeviceToken,

    [property: Required(ErrorMessage = "Cihaz tipi zorunludur.")]
    [property: RegularExpression("^(?i:android|ios|web)$", ErrorMessage = "Cihaz tipi android, ios veya web olmalıdır.")]
    string DeviceType);
