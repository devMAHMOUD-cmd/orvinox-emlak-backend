namespace CraftoraApi.DTOs.Interaction;

public sealed record QaResponseDto(
    Guid Id,
    Guid ProductId,
    Guid UserId,
    string? UserFullName,
    Guid? ParentId,
    string Text,
    DateTime? CreatedAt,
    List<QaResponseDto> Answers);
