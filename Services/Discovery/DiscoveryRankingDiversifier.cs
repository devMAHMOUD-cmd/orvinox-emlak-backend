namespace CraftoraApi.Services.Discovery;

public static class DiscoveryRankingDiversifier
{
    public static IReadOnlyList<Guid> Diversify(
        IReadOnlyList<DiscoveryRankedMediaCandidate> candidates)
    {
        if (candidates.Count < 2)
        {
            return candidates.Select(candidate => candidate.MediaId).ToList();
        }

        var remaining = candidates.ToList();
        var result = new List<Guid>(remaining.Count);
        Guid? previousShopId = null;

        while (remaining.Count > 0)
        {
            var selectedIndex = previousShopId.HasValue
                ? remaining.FindIndex(candidate => candidate.ShopId != previousShopId.Value)
                : 0;
            if (selectedIndex < 0)
            {
                selectedIndex = 0;
            }

            var selected = remaining[selectedIndex];
            remaining.RemoveAt(selectedIndex);
            result.Add(selected.MediaId);
            previousShopId = selected.ShopId;
        }

        return result;
    }
}

public sealed record DiscoveryRankedMediaCandidate(
    Guid MediaId,
    Guid ShopId,
    decimal Score,
    string Reason);
