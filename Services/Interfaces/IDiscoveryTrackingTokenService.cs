namespace CraftoraApi.Services.Interfaces;

public interface IDiscoveryTrackingTokenService
{
    string Issue(
        Guid? userId,
        string contentType,
        Guid contentId,
        Guid shopId,
        Guid feedSessionId,
        int position);

    bool TryValidate(
        string token,
        Guid currentUserId,
        out DiscoveryTrackingContext context);
}

public sealed record DiscoveryTrackingContext(
    Guid TokenId,
    Guid? UserId,
    string ContentType,
    Guid ContentId,
    Guid ShopId,
    Guid FeedSessionId,
    int Position,
    string AlgorithmVersion,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);
