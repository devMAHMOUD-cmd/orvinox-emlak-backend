namespace CraftoraApi.Services.Discovery;

public static class DiscoveryCacheKeys
{
    public const string ReelsRankingVersion = "reels-organic-v1";
    public const string ProductRankingVersion = "organic-v1";
    public const string MixedRankingVersion = "mixed-sponsored-v1";
    public const string BoostVersion = "discovery:boost:version";

    public static string ReelsSnapshot(Guid userId) =>
        $"discovery:reels:snapshot:{ReelsRankingVersion}:user:{userId:D}";

    public static string ProductSnapshot(Guid userId, string contentType) =>
        $"discovery:{contentType}:snapshot:{ProductRankingVersion}:user:{userId:D}";

    public static string MixedSnapshot(Guid userId) =>
        $"discovery:mixed:snapshot:{MixedRankingVersion}:user:{userId:D}";
}
