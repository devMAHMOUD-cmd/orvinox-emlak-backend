using System.ComponentModel.DataAnnotations;
using CraftoraApi.DTOs.Discovery;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class DiscoveryRequestContractTests
{
    [Fact]
    public void Event_batch_rejects_empty_collection()
    {
        var request = new DiscoveryEventBatchRequestDto([]);

        Assert.Contains(
            Validate(request),
            result => result.MemberNames.Contains(nameof(DiscoveryEventBatchRequestDto.Events)));
    }

    [Fact]
    public void Event_batch_rejects_more_than_fifty_events()
    {
        var events = Enumerable
            .Range(0, 51)
            .Select(_ => CreateEvent())
            .ToList();
        var request = new DiscoveryEventBatchRequestDto(events);

        Assert.Contains(
            Validate(request),
            result => result.MemberNames.Contains(nameof(DiscoveryEventBatchRequestDto.Events)));
    }

    [Fact]
    public void Event_contract_rejects_out_of_range_metrics()
    {
        var request = CreateEvent() with
        {
            DwellMs = -1,
            CompletionRate = 1.01m,
            VisiblePercentage = 101
        };

        var results = Validate(request);

        Assert.Contains(results, result => result.MemberNames.Contains(nameof(request.DwellMs)));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(request.CompletionRate)));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(request.VisiblePercentage)));
    }

    [Fact]
    public void Feedback_contract_rejects_oversized_tracking_token()
    {
        var request = new DiscoveryFeedbackRequestDto(
            EventId: Guid.NewGuid(),
            FeedbackType: "not_interested",
            TrackingToken: new string('a', 4097));

        Assert.Contains(
            Validate(request),
            result => result.MemberNames.Contains(nameof(request.TrackingToken)));
    }

    private static DiscoveryEventRequestDto CreateEvent()
    {
        return new DiscoveryEventRequestDto(
            EventId: Guid.NewGuid(),
            EventType: "impression",
            TrackingToken: "signed-token",
            DwellMs: 500,
            CompletionRate: null,
            VisiblePercentage: 50,
            Metadata: null);
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
