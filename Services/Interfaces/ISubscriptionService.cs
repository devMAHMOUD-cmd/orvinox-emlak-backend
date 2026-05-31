using CraftoraApi.DTOs.Subscription;

namespace CraftoraApi.Services.Interfaces;

public interface ISubscriptionService
{
    Task<SubscriptionResponseDto?> GetMySubscriptionAsync(Guid userId);

    Task<SubscriptionResponseDto> StartSubscriptionAsync(Guid userId, StartSubscriptionRequestDto request);

    Task<SubscriptionResponseDto> CancelSubscriptionAsync(Guid userId);
}
