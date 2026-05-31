namespace CraftoraApi.Models.Elasticsearch;

public sealed record ProductSyncMessage(
    Guid ProductId,
    string Action,
    ProductDocument? Document);
