namespace CraftoraApi.DTOs.Customer;

public sealed record SellerCustomerListItemDto(
    Guid CustomerId,
    string? Name,
    string Email,
    string? AvatarUrl,
    string Type,
    int TotalOrders,
    decimal TotalSpent,
    string? Currency,
    DateTime? LastActivityAt,
    string? LastActivityType,
    bool IsSubscriber,
    int CourseCount,
    int ProductViewCount,
    int ShopVisitCount);
