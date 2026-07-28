using CraftoraApi.DTOs.Discovery;

namespace CraftoraApi.Services.Interfaces;

public interface IDiscoveryEventService
{
    Task<DiscoveryEventBatchResponseDto> RecordBatchAsync(
        Guid userId,
        DiscoveryEventBatchRequestDto request,
        CancellationToken cancellationToken = default);

    Task<DiscoveryFeedbackResponseDto> SetFeedbackAsync(
        Guid userId,
        DiscoveryFeedbackRequestDto request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DiscoveryFeedbackResponseDto>> GetFeedbackAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task RemoveFeedbackAsync(
        Guid userId,
        Guid feedbackId,
        CancellationToken cancellationToken = default);
}
