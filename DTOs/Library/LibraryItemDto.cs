namespace CraftoraApi.DTOs.Library;

public sealed record LibraryItemDto(
    Guid Id,
    Guid ProductId,
    string ProductTitle,
    string? CoverImageUrl,
    DateTime? PurchasedAt,
    DateTime? LastAccessedAt);
