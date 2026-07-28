namespace CraftoraApi.Services.Discovery;

public static class DiscoveryFeedMixer
{
    private static readonly string[] OrganicPattern =
        ["media", "media", "product", "media", "course"];
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

            var selectedType = FindTypeWithDifferentShop(
                    queues,
                    preferredType,
                    previousShopId)
                ?? FindAvailableType(queues, preferredType);
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

    private static string? FindTypeWithDifferentShop(
        IReadOnlyDictionary<string, List<DiscoveryFeedCandidate>> queues,
        string preferredType,
        Guid? previousShopId)
    {
        if (!previousShopId.HasValue)
        {
            return queues[preferredType].Count > 0 ? preferredType : null;
        }

        if (queues[preferredType].Any(item => item.ShopId != previousShopId.Value))
        {
            return preferredType;
        }

        return ContentTypes.FirstOrDefault(type =>
            queues[type].Any(item => item.ShopId != previousShopId.Value));
    }

    private static string? FindAvailableType(
        IReadOnlyDictionary<string, List<DiscoveryFeedCandidate>> queues,
        string preferredType)
    {
        if (queues[preferredType].Count > 0)
        {
            return preferredType;
        }

        return ContentTypes.FirstOrDefault(type => queues[type].Count > 0);
    }
}

public sealed record DiscoveryFeedCandidate(
    string ContentType,
    Guid ContentId,
    Guid ShopId);
