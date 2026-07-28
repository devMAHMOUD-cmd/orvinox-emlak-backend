using CraftoraApi.Services.Discovery;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class DiscoveryFeedMixerTests
{
    [Fact]
    public void Mixer_uses_the_media_weighted_organic_pattern()
    {
        var media = CreateCandidates("media", 6);
        var products = CreateCandidates("product", 2);
        var courses = CreateCandidates("course", 2);

        var result = DiscoveryFeedMixer.Mix(media, products, courses);

        Assert.Equal(
            [
                "media", "media", "product", "media", "course",
                "media", "media", "product", "media", "course"
            ],
            result.Select(item => item.ContentType));
    }

    [Fact]
    public void Mixer_avoids_an_adjacent_shop_when_an_alternative_exists()
    {
        var shopA = Guid.NewGuid();
        var shopB = Guid.NewGuid();
        var media = new List<DiscoveryFeedCandidate>
        {
            new("media", Guid.NewGuid(), shopA),
            new("media", Guid.NewGuid(), shopA)
        };
        var products = new List<DiscoveryFeedCandidate>
        {
            new("product", Guid.NewGuid(), shopB)
        };

        var result = DiscoveryFeedMixer.Mix(media, products, []);

        Assert.Equal(shopA, result[0].ShopId);
        Assert.Equal(shopB, result[1].ShopId);
        Assert.Equal(shopA, result[2].ShopId);
    }

    [Fact]
    public void Mixer_preserves_every_candidate_once_when_a_pool_is_small()
    {
        var media = CreateCandidates("media", 2);
        var products = CreateCandidates("product", 1);
        var courses = CreateCandidates("course", 3);
        var expectedIds = media
            .Concat(products)
            .Concat(courses)
            .Select(item => item.ContentId)
            .ToHashSet();

        var result = DiscoveryFeedMixer.Mix(media, products, courses);

        Assert.Equal(expectedIds.Count, result.Count);
        Assert.Equal(expectedIds, result.Select(item => item.ContentId).ToHashSet());
    }

    private static List<DiscoveryFeedCandidate> CreateCandidates(
        string contentType,
        int count)
    {
        return Enumerable.Range(0, count)
            .Select(_ => new DiscoveryFeedCandidate(
                contentType,
                Guid.NewGuid(),
                Guid.NewGuid()))
            .ToList();
    }
}
