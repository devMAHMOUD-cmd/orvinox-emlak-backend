namespace CraftoraApi.DTOs.Library;

public sealed record LibraryItemDto(
    Guid Id,
    Guid ProductId,
    string ProductTitle,
    string ProductType,
    string? CoverImageUrl,
    string? CoverImagePublicUrl,
    string ShopName,
    bool HasProductFile,
    string? ProductFileName,
    bool ProductIsActive,
    bool IsArchived,
    DateTime? PurchasedAt,
    DateTime? LastAccessedAt);
