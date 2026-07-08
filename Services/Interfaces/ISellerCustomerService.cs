using CraftoraApi.DTOs.Customer;

namespace CraftoraApi.Services.Interfaces;

public interface ISellerCustomerService
{
    Task<SellerCustomerListResponseDto> GetCustomersAsync(
        Guid userId,
        string? type,
        DateTime? startDate,
        DateTime? endDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<SellerCustomerDetailDto> GetCustomerDetailAsync(
        Guid userId,
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task<SellerCustomerSummaryDto> GetSummaryAsync(
        Guid userId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SellerCustomerSegmentDto>> GetSegmentsAsync(
        Guid userId,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default);
}
