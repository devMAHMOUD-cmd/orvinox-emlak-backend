using CraftoraApi.Services.Discovery;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class DiscoveryFeedCursorTests
{
    private static readonly Guid UserId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherUserId =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid FeedSessionId =
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public void Cursor_round_trips_user_session_and_offset()
    {
        var service = CreateService();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);

        var token = service.Issue(UserId, FeedSessionId, 17, expiresAt);

        Assert.True(service.TryRead(token, UserId, out var context));
        Assert.Equal(UserId, context.UserId);
        Assert.Equal(FeedSessionId, context.FeedSessionId);
        Assert.Equal(17, context.Offset);
        Assert.Equal(expiresAt.ToUnixTimeSeconds(), context.ExpiresAt.ToUnixTimeSeconds());
    }

    [Fact]
    public void Cursor_is_bound_to_the_authenticated_user()
    {
        var service = CreateService();
        var token = service.Issue(
            UserId,
            FeedSessionId,
            10,
            DateTimeOffset.UtcNow.AddMinutes(30));

        Assert.False(service.TryRead(token, OtherUserId, out _));
    }

    [Fact]
    public void Tampered_cursor_is_rejected()
    {
        var service = CreateService();
        var token = service.Issue(
            UserId,
            FeedSessionId,
            10,
            DateTimeOffset.UtcNow.AddMinutes(30));
        var tampered = token[..^1] + (token[^1] == 'A' ? "B" : "A");

        Assert.False(service.TryRead(tampered, UserId, out _));
    }

    [Fact]
    public void Expired_cursor_is_rejected()
    {
        var service = CreateService();
        var token = service.Issue(
            UserId,
            FeedSessionId,
            10,
            DateTimeOffset.UtcNow.AddSeconds(-1));

        Assert.False(service.TryRead(token, UserId, out _));
    }

    private static DiscoveryFeedCursorService CreateService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Discovery:TrackingSecret"] =
                    "discovery-feed-cursor-test-secret-with-at-least-32-characters"
            })
            .Build();

        return new DiscoveryFeedCursorService(configuration);
    }
}
