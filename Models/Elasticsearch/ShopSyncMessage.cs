namespace CraftoraApi.Models.Elasticsearch;

public sealed record ShopSyncMessage(
    Guid ShopId,
    string Action,
    ShopDocument? Document);
