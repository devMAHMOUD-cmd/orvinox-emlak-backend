using CraftoraApi.Infrastructure.Messaging.Contracts;

namespace CraftoraApi.Infrastructure.Services;

public interface IVideoProcessingService
{
    Task<VideoProcessingResult> ProcessVideoAsync(
        ProcessVideoCommand command,
        CancellationToken cancellationToken = default);
}
