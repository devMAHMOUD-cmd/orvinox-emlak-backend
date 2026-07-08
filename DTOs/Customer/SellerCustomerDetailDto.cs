namespace CraftoraApi.DTOs.Customer;

public sealed record SellerCustomerDetailDto(
    Guid CustomerId,
    string? Name,
    string Email,
    string? AvatarUrl,
    DateTime? JoinedAt,
    int TotalOrders,
    decimal TotalSpent,
    decimal AverageOrderValue,
    bool IsSubscriber,
    string? SubscriptionStatus,
    DateTime? LastActivityAt,
    IReadOnlyList<SellerCustomerOrderDto> Orders,
    IReadOnlyList<SellerCustomerActivityDto> Activities);
