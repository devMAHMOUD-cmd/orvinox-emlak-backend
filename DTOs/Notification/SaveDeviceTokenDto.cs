using System.ComponentModel.DataAnnotations;

namespace CraftoraApi.DTOs.Notification;

public sealed record SaveDeviceTokenDto(
    [property: Required(ErrorMessage = "Cihaz token bilgisi zorunludur.")]
    string DeviceToken,

    [property: Required(ErrorMessage = "Cihaz tipi zorunludur.")]
    string DeviceType);
