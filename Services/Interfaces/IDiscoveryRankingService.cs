namespace CraftoraApi.Services.Interfaces;

public interface IDiscoveryRankingService
{
    Task<IReadOnlyList<Guid>> GetPersonalizedMediaIdsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task InvalidateMediaSnapshotAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
