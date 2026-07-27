using CraftoraApi.DTOs.Admin;

namespace CraftoraApi.Services.Interfaces;

public interface IAdminEmailCampaignService
{
    Task<AdminEmailCampaignPreviewDto> PreviewAsync(
        AdminEmailCampaignPreviewRequestDto request,
        CancellationToken cancellationToken = default);

    Task<AdminEmailCampaignDto> CreateAndDispatchAsync(
        Guid adminUserId,
        AdminEmailCampaignSendRequestDto request,
        CancellationToken cancellationToken = default);

    Task<AdminEmailCampaignDto> GetAsync(
        Guid adminUserId,
        Guid campaignId,
        CancellationToken cancellationToken = default);

    Task<AdminPagedResponseDto<AdminEmailCampaignDto>> GetListAsync(
        Guid adminUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<AdminEmailCampaignDto> RetryFailedAsync(
        Guid adminUserId,
        Guid campaignId,
        CancellationToken cancellationToken = default);
}
