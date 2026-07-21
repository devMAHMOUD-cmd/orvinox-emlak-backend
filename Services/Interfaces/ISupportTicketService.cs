using CraftoraApi.DTOs.Support;

namespace CraftoraApi.Services.Interfaces;

public interface ISupportTicketService
{
    Task<TicketDetailDto> CreateTicketAsync(Guid userId, CreateTicketDto dto);

    Task<TicketListResponseDto> GetMyTicketsAsync(Guid userId, string? status, int page, int pageSize);

    Task<TicketDetailDto> GetMyTicketDetailAsync(Guid userId, Guid ticketId);

    Task<SupportMessageResponseDto> AddMessageAsync(Guid userId, Guid ticketId, AddMessageDto dto);

    Task<AdminTicketListResponseDto> GetAllTicketsAsync(string? status, string? query, int page, int pageSize);

    Task<AdminTicketDetailDto> GetTicketDetailAsync(Guid ticketId);

    Task<AdminTicketMessageDto> AddAdminReplyAsync(Guid adminUserId, Guid ticketId, AdminReplyDto dto);

    Task<AdminTicketDetailDto> UpdateStatusAsync(Guid adminUserId, Guid ticketId, UpdateTicketStatusDto dto);
}
