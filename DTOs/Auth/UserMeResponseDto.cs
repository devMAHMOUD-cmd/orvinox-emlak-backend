namespace CraftoraApi.DTOs.Auth;

public sealed record UserMeResponseDto(
    Guid Id,
    string FullName,
    string Email,
    string Role,
    bool HasShop,
    Guid? ShopId,
    string? ShopSlug,
    bool? ShopIsActive);
