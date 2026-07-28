namespace CraftoraApi.Services.Interfaces;

public interface IDiscoveryFeedCursorService
{
    string Issue(
        Guid userId,
        Guid feedSessionId,
        int offset,
        DateTimeOffset expiresAt);

    bool TryRead(
        string token,
        Guid expectedUserId,
        out DiscoveryFeedCursorContext context);
}

public sealed record DiscoveryFeedCursorContext(
    Guid UserId,
    Guid FeedSessionId,
    int Offset,
    DateTimeOffset ExpiresAt);
