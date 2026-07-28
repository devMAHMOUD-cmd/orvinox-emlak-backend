using CraftoraApi.Services.Discovery;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class DiscoveryTrackingTokenTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ContentId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid ShopId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid FeedSessionId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [Fact]
    public void Issued_token_round_trips_authoritative_context()
    {
        var service = CreateService();

        var token = service.Issue(
            UserId,
            "MEDIA",
            ContentId,
            ShopId,
            FeedSessionId,
            position: 7);

        Assert.True(service.TryValidate(token, UserId, out var context));
        Assert.Equal(UserId, context.UserId);
        Assert.Equal("media", context.ContentType);
        Assert.Equal(ContentId, context.ContentId);
        Assert.Equal(ShopId, context.ShopId);
        Assert.Equal(FeedSessionId, context.FeedSessionId);
        Assert.Equal(7, context.Position);
        Assert.Equal(DiscoveryTrackingTokenService.CurrentAlgorithmVersion, context.AlgorithmVersion);
        Assert.False(context.IsSponsored);
        Assert.Null(context.BoostId);
    }

    [Fact]
    public void Sponsored_token_round_trips_authoritative_boost_context()
    {
        var service = CreateService();
        var boostId = Guid.NewGuid();

        var token = service.Issue(
            UserId,
            "product",
            ContentId,
            ShopId,
            FeedSessionId,
            position: 9,
            isSponsored: true,
            boostId);

        Assert.True(service.TryValidate(token, UserId, out var context));
        Assert.True(context.IsSponsored);
        Assert.Equal(boostId, context.BoostId);
        Assert.Equal(9, context.Position);
    }

    [Fact]
    public void Sponsored_token_requires_a_non_empty_boost_id()
    {
        var service = CreateService();

        Assert.Throws<ArgumentException>(() => service.Issue(
            UserId,
            "product",
            ContentId,
            ShopId,
            FeedSessionId,
            position: 9,
            isSponsored: true,
            boostId: null));

        Assert.Throws<ArgumentException>(() => service.Issue(
            UserId,
            "product",
            ContentId,
            ShopId,
            FeedSessionId,
            position: 9,
            isSponsored: true,
            boostId: Guid.Empty));
    }

    [Fact]
    public void User_bound_token_is_rejected_for_another_user()
    {
        var service = CreateService();
        var token = service.Issue(
            UserId,
            "media",
            ContentId,
            ShopId,
            FeedSessionId,
            position: 0);

        Assert.False(service.TryValidate(token, OtherUserId, out _));
    }

    [Fact]
    public void Anonymous_feed_token_can_be_claimed_by_authenticated_user()
    {
        var service = CreateService();
        var token = service.Issue(
            userId: null,
            "media",
            ContentId,
            ShopId,
            FeedSessionId,
            position: 0);

        Assert.True(service.TryValidate(token, UserId, out var context));
        Assert.Null(context.UserId);
    }

    [Fact]
    public void Tampered_token_is_rejected()
    {
        var service = CreateService();
        var token = service.Issue(
            UserId,
            "media",
            ContentId,
            ShopId,
            FeedSessionId,
            position: 0);
        var tampered = token[..^1] + (token[^1] == 'A' ? "B" : "A");

        Assert.False(service.TryValidate(tampered, UserId, out _));
    }

    [Fact]
    public void Unsupported_content_type_is_rejected_when_issuing()
    {
        var service = CreateService();

        Assert.Throws<ArgumentException>(() => service.Issue(
            UserId,
            "unknown",
            ContentId,
            ShopId,
            FeedSessionId,
            position: 0));
    }

    private static DiscoveryTrackingTokenService CreateService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Discovery:TrackingSecret"] =
                    "discovery-contract-test-secret-with-at-least-32-characters"
            })
            .Build();

        return new DiscoveryTrackingTokenService(configuration);
    }
}
