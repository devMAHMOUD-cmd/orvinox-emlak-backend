using System.ComponentModel.DataAnnotations;
using CraftoraApi.DTOs.Analytics;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class AnalyticsRequestContractTests
{
    [Fact]
    public void Analytics_event_rejects_missing_event_type()
    {
        var request = new TrackAnalyticsEventDto(
            EventType: string.Empty,
            ShopId: Guid.NewGuid(),
            ProductId: null,
            MediaId: null,
            OrderId: null,
            SessionId: null,
            Source: null,
            Referrer: null,
            UtmSource: null,
            UtmMedium: null,
            UtmCampaign: null,
            DeviceType: null,
            Metadata: null);

        Assert.Contains(
            Validate(request),
            result => result.MemberNames.Contains(nameof(TrackAnalyticsEventDto.EventType)));
    }

    [Fact]
    public void Analytics_event_rejects_oversized_source()
    {
        var request = new TrackAnalyticsEventDto(
            EventType: "shop_visit",
            ShopId: Guid.NewGuid(),
            ProductId: null,
            MediaId: null,
            OrderId: null,
            SessionId: null,
            Source: new string('a', 101),
            Referrer: null,
            UtmSource: null,
            UtmMedium: null,
            UtmCampaign: null,
            DeviceType: null,
            Metadata: null);

        Assert.Contains(
            Validate(request),
            result => result.MemberNames.Contains(nameof(TrackAnalyticsEventDto.Source)));
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            instance,
            new ValidationContext(instance),
            results,
            validateAllProperties: true);
        return results;
    }
}
