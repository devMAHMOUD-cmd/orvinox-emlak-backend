using CraftoraApi.Services.Discovery;
using Xunit;

namespace CraftoraApi.Tests;

public sealed class DiscoveryRankingDiversifierTests
{
    [Fact]
    public void Diversifier_avoids_consecutive_shops_when_an_alternative_exists()
    {
        var shopA = Guid.NewGuid();
        var shopB = Guid.NewGuid();
        var candidates = new List<DiscoveryRankedMediaCandidate>
        {
            CreateCandidate(shopA, 10),
            CreateCandidate(shopA, 9),
            CreateCandidate(shopB, 8),
            CreateCandidate(shopB, 7)
        };

        var result = DiscoveryRankingDiversifier.Diversify(candidates);
        var shopsByMediaId = candidates.ToDictionary(item => item.MediaId, item => item.ShopId);

        Assert.Equal(4, result.Count);
        Assert.NotEqual(shopsByMediaId[result[0]], shopsByMediaId[result[1]]);
        Assert.NotEqual(shopsByMediaId[result[1]], shopsByMediaId[result[2]]);
        Assert.NotEqual(shopsByMediaId[result[2]], shopsByMediaId[result[3]]);
    }

    [Fact]
    public void Diversifier_preserves_all_candidates_when_only_one_shop_exists()
    {
        var shopId = Guid.NewGuid();
        var candidates = new List<DiscoveryRankedMediaCandidate>
        {
            CreateCandidate(shopId, 10),
            CreateCandidate(shopId, 9),
            CreateCandidate(shopId, 8)
        };

        var result = DiscoveryRankingDiversifier.Diversify(candidates);

        Assert.Equal(candidates.Select(item => item.MediaId), result);
    }

    private static DiscoveryRankedMediaCandidate CreateCandidate(Guid shopId, decimal score)
    {
        return new DiscoveryRankedMediaCandidate(
            Guid.NewGuid(),
            shopId,
            score,
            "test");
    }
}
