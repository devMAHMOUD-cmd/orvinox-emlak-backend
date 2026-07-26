namespace CraftoraApi.Redis;

public static class CacheKeys
{
    public static string PublicShopBySlug(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        return $"shop:public:slug:v2:{slug.Trim().ToLowerInvariant()}";
    }
}
