namespace CraftoraApi.Services.Discovery;

public static class DiscoveryFeedMixer
{
    private static readonly string[] OrganicPattern =
        ["media", "product", "course"];
    private static readonly string[] ContentTypes = ["media", "product", "course"];

    public static IReadOnlyList<DiscoveryFeedCandidate> Mix(
        IReadOnlyList<DiscoveryFeedCandidate> media,
        IReadOnlyList<DiscoveryFeedCandidate> products,
        IReadOnlyList<DiscoveryFeedCandidate> courses)
    {
        var queues = new Dictionary<string, List<DiscoveryFeedCandidate>>(
            StringComparer.Ordinal)
        {
            ["media"] = media.ToList(),
            ["product"] = products.ToList(),
            ["course"] = courses.ToList()
        };
        var result = new List<DiscoveryFeedCandidate>(
            media.Count + products.Count + courses.Count);
        var patternIndex = 0;
        Guid? previousShopId = null;

        while (queues.Values.Any(queue => queue.Count > 0))
        {
            var preferredType = OrganicPattern[patternIndex % OrganicPattern.Length];
            patternIndex++;

            var selectedType = queues[preferredType].Count > 0
                ? preferredType
                : FindTypeWithDifferentShop(
                    queues,
                    previousShopId)
                  ?? FindAvailableType(queues);
            if (selectedType is null)
            {
                break;
            }

            var queue = queues[selectedType];
            var candidateIndex = previousShopId.HasValue
                ? queue.FindIndex(item => item.ShopId != previousShopId.Value)
                : 0;
            if (candidateIndex < 0)
            {
                candidateIndex = 0;
            }

            var candidate = queue[candidateIndex];
            queue.RemoveAt(candidateIndex);
            result.Add(candidate);
            previousShopId = candidate.ShopId;
        }

        return result;
    }

    public static IReadOnlyList<DiscoveryFeedCandidate> InsertSponsored(
        IReadOnlyList<DiscoveryFeedCandidate> organic,
        IReadOnlyList<DiscoveryFeedCandidate> sponsored)
    {
        if (sponsored.Count == 0)
        {
            return organic;
        }

        var sponsoredKeys = sponsored
            .Select(item => (item.ContentType, item.ContentId))
            .ToHashSet();
        var result = organic
            .Where(item => !sponsoredKeys.Contains((item.ContentType, item.ContentId)))
            .ToList();

        for (var index = 0; index < sponsored.Count; index++)
        {
            var insertionIndex = Math.Min(9 + index * 10, result.Count);
            result.Insert(insertionIndex, sponsored[index]);
        }

        return result;
    }

    private static string? FindTypeWithDifferentShop(
        IReadOnlyDictionary<string, List<DiscoveryFeedCandidate>> queues,
        Guid? previousShopId)
    {
        if (!previousShopId.HasValue) return null;

        return ContentTypes.FirstOrDefault(type =>
            queues[type].Any(item => item.ShopId != previousShopId.Value));
    }

    private static string? FindAvailableType(
        IReadOnlyDictionary<string, List<DiscoveryFeedCandidate>> queues)
    {
        return ContentTypes.FirstOrDefault(type => queues[type].Count > 0);
    }
}

public sealed record DiscoveryFeedCandidate(
    string ContentType,
    Guid ContentId,
    Guid ShopId,
    bool IsSponsored = false,
    Guid? BoostId = null);
