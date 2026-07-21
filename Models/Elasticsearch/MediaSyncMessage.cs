namespace CraftoraApi.Models.Elasticsearch;

public sealed record MediaSyncMessage(
    Guid MediaId,
    string Action,
    MediaDocument? Document);
