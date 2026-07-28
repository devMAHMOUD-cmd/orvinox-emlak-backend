using CraftoraApi.DTOs.Discovery;

namespace CraftoraApi.Services.Interfaces;

public interface IDiscoveryFeedService
{
    Task<DiscoveryFeedResponseDto> GetFeedAsync(
        Guid userId,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default);
}
