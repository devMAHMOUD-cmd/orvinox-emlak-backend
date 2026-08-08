using CraftoraApi.DTOs.Subscription;

namespace CraftoraApi.Services.Interfaces;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanResponseDto>> GetPlansAsync();

    Task<SubscriptionResponseDto?> GetMySubscriptionAsync(Guid userId);

    Task<SubscriptionResponseDto> StartSubscriptionAsync(Guid userId, StartSubscriptionRequestDto request);

    Task<SubscriptionResponseDto> StartShopSubscriptionAsync(
        Guid userId,
        StartShopSubscriptionRequestDto request);

    Task<SubscriptionResponseDto> CancelSubscriptionAsync(Guid userId);
}
