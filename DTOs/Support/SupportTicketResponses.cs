namespace CraftoraApi.DTOs.Support;

public sealed record TicketListResponseDto(
    IReadOnlyList<TicketListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record TicketListItemDto(
    Guid Id,
    string Subject,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime LastMessageAt,
    string? LastMessageSenderRole,
    string? LastMessagePreview,
    int UnreadCount);

public sealed record TicketDetailDto(
    Guid Id,
    string Subject,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime LastMessageAt,
    DateTime? ClosedAt,
    IReadOnlyList<TicketMessageDto> Messages);

public sealed record TicketMessageDto(
    Guid Id,
    Guid SenderUserId,
    string SenderRole,
    string? SenderName,
    string Message,
    DateTime CreatedAt);

public sealed record SupportMessageResponseDto(
    TicketMessageDto Message,
    string TicketStatus);

public sealed record AdminTicketListResponseDto(
    IReadOnlyList<AdminTicketListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record AdminTicketListItemDto(
    Guid Id,
    string Subject,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime LastMessageAt,
    string? LastMessageSenderRole,
    Guid UserId,
    string? UserFullName,
    string UserEmail);

public sealed record AdminTicketDetailDto(
    Guid Id,
    string Subject,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime LastMessageAt,
    DateTime? ClosedAt,
    Guid? ClosedByUserId,
    Guid UserId,
    string? UserFullName,
    string UserEmail,
    IReadOnlyList<AdminTicketMessageDto> Messages);

public sealed record AdminTicketMessageDto(
    Guid Id,
    Guid SenderId,
    string SenderRole,
    string? SenderName,
    string SenderEmail,
    string Message,
    DateTime CreatedAt);
