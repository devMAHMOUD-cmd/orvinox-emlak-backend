namespace CraftoraApi.DTOs.Category;

public sealed record CategoryResponseDto(
    Guid Id,
    string Name,
    string Slug,
    Guid? ParentId);
