using System.ComponentModel.DataAnnotations;
using CraftoraApi.DTOs.Notification;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class NotificationContractTests
{
    [Fact]
    public void Device_token_rejects_short_token()
    {
        var result = Validate(new SaveDeviceTokenDto(
            DeviceToken: "short",
            DeviceType: "android"));

        Assert.Contains(
            result,
            validation => validation.MemberNames.Contains(nameof(SaveDeviceTokenDto.DeviceToken)));
    }

    [Fact]
    public void Device_token_rejects_unknown_device_type()
    {
        var result = Validate(new SaveDeviceTokenDto(
            DeviceToken: "e2e-device-token-1234567890",
            DeviceType: "desktop"));

        Assert.Contains(
            result,
            validation => validation.MemberNames.Contains(nameof(SaveDeviceTokenDto.DeviceType)));
    }

    [Fact]
    public void Device_token_accepts_supported_device_type()
    {
        var result = Validate(new SaveDeviceTokenDto(
            DeviceToken: "e2e-device-token-1234567890",
            DeviceType: "Android"));

        Assert.Empty(result);
    }

    private static List<ValidationResult> Validate(SaveDeviceTokenDto dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            dto,
            new ValidationContext(dto),
            results,
            validateAllProperties: true);
        return results;
    }
}
