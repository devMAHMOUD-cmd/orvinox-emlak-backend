namespace CraftoraApi.Services.Interfaces;

public interface IAdminCampaignEmailDeliveryService
{
    Task DeliverAsync(Guid recipientId, CancellationToken cancellationToken = default);
}
