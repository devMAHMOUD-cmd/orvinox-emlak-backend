namespace CraftoraApi.DTOs.Interaction;

public sealed record ReviewResponseDto(
    Guid Id,
    Guid ProductId,
    Guid UserId,
    string? UserFullName,
    int Rating,
    string? Comment,
    List<string> Images,
    string? SellerReply,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);
