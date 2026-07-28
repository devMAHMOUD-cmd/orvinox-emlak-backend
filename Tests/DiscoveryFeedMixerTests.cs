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

    [Fact]
    public void Sponsored_item_occupies_the_tenth_slot_without_duplication()
    {
        var organic = CreateCandidates("media", 10);
        var target = organic[3];
        var boostId = Guid.NewGuid();
        var sponsored = target with
        {
            IsSponsored = true,
            BoostId = boostId
        };

        var result = DiscoveryFeedMixer.InsertSponsored(organic, [sponsored]);

        Assert.Equal(10, result.Count);
        Assert.Equal(target.ContentId, result[9].ContentId);
        Assert.True(result[9].IsSponsored);
        Assert.Equal(boostId, result[9].BoostId);
        Assert.Single(result, item => item.ContentId == target.ContentId);
        Assert.Single(result, item => item.IsSponsored);
    }

    [Fact]
    public void Sponsored_items_are_spaced_one_per_ten_slots()
    {
        var organic = CreateCandidates("media", 20);
        var sponsored = CreateCandidates("product", 2)
            .Select(item => item with
            {
                IsSponsored = true,
                BoostId = Guid.NewGuid()
            })
            .ToList();

        var result = DiscoveryFeedMixer.InsertSponsored(organic, sponsored);

        Assert.Equal(22, result.Count);
        Assert.True(result[9].IsSponsored);
        Assert.True(result[19].IsSponsored);
        Assert.Equal(2, result.Count(item => item.IsSponsored));
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
