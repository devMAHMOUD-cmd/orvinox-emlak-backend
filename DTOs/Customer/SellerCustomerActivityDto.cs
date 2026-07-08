namespace CraftoraApi.DTOs.Customer;

public sealed record SellerCustomerActivityDto(
    Guid Id,
    string Type,
    string Title,
    Guid? TargetId,
    DateTime? CreatedAt);
