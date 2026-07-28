using CraftoraApi.DTOs.Admin;

namespace CraftoraApi.Services.Interfaces;

public interface IAdminDiscoveryBoostService
{
    Task<AdminDiscoveryBoostDto> SetAsync(
        Guid adminUserId,
        AdminDiscoveryBoostRequestDto request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminDiscoveryBoostDto>> GetListAsync(
        Guid adminUserId,
        CancellationToken cancellationToken = default);

    Task StopAsync(
        Guid adminUserId,
        Guid boostId,
        CancellationToken cancellationToken = default);
}
