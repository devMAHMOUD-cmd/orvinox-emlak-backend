namespace CraftoraApi.Services.Interfaces;

public interface IDiscoveryRankingService
{
    Task<IReadOnlyList<Guid>> GetPersonalizedMediaIdsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetPersonalizedProductIdsAsync(
        Guid userId,
        string contentType,
        CancellationToken cancellationToken = default);

    Task InvalidateSnapshotsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
