using CraftoraApi.Data;
using CraftoraApi.DTOs.Support;
using CraftoraApi.Infrastructure.Security;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class SupportTicketService : ISupportTicketService
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;
    private const int MaximumSearchLength = 100;
    private const int MaximumSubjectLength = 200;
    private const int MaximumMessageLength = 5000;

    private readonly AppDbContext _dbContext;

    public SupportTicketService(AppDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<TicketDetailDto> CreateTicketAsync(Guid userId, CreateTicketDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var subject = PlainTextInputValidator.Require(dto.Subject, "Ticket konusu", MaximumSubjectLength);
        var messageText = PlainTextInputValidator.Require(dto.Message, "Ticket mesaji", MaximumMessageLength);
        await EnsureActiveUserAsync(userId);

        var now = DateTime.UtcNow;
        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Subject = subject,
            Status = SupportTicketStatus.Open,
            CreatedAt = now,
            UpdatedAt = now,
            LastMessageAt = now
        };

        var message = new SupportTicketMessage
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            SenderId = userId,
            SenderRole = SupportMessageSenderRole.User,
            Message = messageText,
            CreatedAt = now
        };

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        _dbContext.SupportTickets.Add(ticket);
        _dbContext.SupportTicketMessages.Add(message);
        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        return await GetMyTicketDetailAsync(userId, ticket.Id);
    }

    public async Task<TicketListResponseDto> GetMyTicketsAsync(Guid userId, string? status, int page, int pageSize)
    {
        await EnsureActiveUserAsync(userId);

        var requestedStatus = ParseOptionalStatus(status);
        var (normalizedPage, normalizedPageSize) = NormalizePagination(page, pageSize);

        var tickets = _dbContext.SupportTickets
            .AsNoTracking()
            .Where(ticket => ticket.UserId == userId);

        if (requestedStatus.HasValue)
        {
            tickets = tickets.Where(ticket => ticket.Status == requestedStatus.Value);
        }

        var totalCount = await tickets.CountAsync();
        var ticketRows = await tickets
            .OrderByDescending(ticket => ticket.LastMessageAt)
            .ThenByDescending(ticket => ticket.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(ticket => new
            {
                ticket.Id,
                ticket.Subject,
                ticket.Status,
                ticket.CreatedAt,
                ticket.UpdatedAt,
                ticket.LastMessageAt,
                LastMessageSenderRole = ticket.Messages
                    .OrderByDescending(message => message.CreatedAt)
                    .Select(message => (SupportMessageSenderRole?)message.SenderRole)
                    .FirstOrDefault()
            })
            .ToListAsync();

        var items = ticketRows
            .Select(ticket => new TicketListItemDto(
                ticket.Id,
                ticket.Subject,
                ToApiValue(ticket.Status),
                ticket.CreatedAt,
                ticket.UpdatedAt,
                ticket.LastMessageAt,
                ticket.LastMessageSenderRole is null ? null : ToApiValue(ticket.LastMessageSenderRole.Value)))
            .ToList();

        return new TicketListResponseDto(
            items,
            normalizedPage,
            normalizedPageSize,
            totalCount,
            CalculateTotalPages(totalCount, normalizedPageSize));
    }

    public async Task<TicketDetailDto> GetMyTicketDetailAsync(Guid userId, Guid ticketId)
    {
        await EnsureActiveUserAsync(userId);

        var ticket = await _dbContext.SupportTickets
            .AsNoTracking()
            .Include(item => item.Messages)
                .ThenInclude(message => message.Sender)
            .FirstOrDefaultAsync(item => item.Id == ticketId && item.UserId == userId);

        return ticket is null
            ? throw new NotFoundException("Destek talebi bulunamadi.")
            : MapToUserDetail(ticket);
    }

    public async Task<TicketMessageDto> AddMessageAsync(Guid userId, Guid ticketId, AddMessageDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var messageText = PlainTextInputValidator.Require(dto.Message, "Ticket mesaji", MaximumMessageLength);
        var user = await EnsureActiveUserAsync(userId);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        var ticket = await GetTicketForUpdateAsync(ticketId);

        if (ticket.UserId != userId)
        {
            throw new NotFoundException("Destek talebi bulunamadi.");
        }

        if (ticket.Status == SupportTicketStatus.Closed)
        {
            throw new ConflictException("Kapali destek talebine yeni mesaj ekleyemezsiniz.");
        }

        var now = DateTime.UtcNow;
        var message = new SupportTicketMessage
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            SenderId = userId,
            SenderRole = SupportMessageSenderRole.User,
            Message = messageText,
            CreatedAt = now
        };

        ticket.Status = SupportTicketStatus.Open;
        ticket.LastMessageAt = now;
        ticket.UpdatedAt = now;
        _dbContext.SupportTicketMessages.Add(message);

        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        return new TicketMessageDto(
            message.Id,
            ToApiValue(message.SenderRole),
            user.FullName,
            message.Message,
            message.CreatedAt);
    }

    public async Task<AdminTicketListResponseDto> GetAllTicketsAsync(string? status, string? query, int page, int pageSize)
    {
        var requestedStatus = ParseOptionalStatus(status);
        var (normalizedPage, normalizedPageSize) = NormalizePagination(page, pageSize);
        var normalizedQuery = NormalizeSearchQuery(query);

        var tickets = _dbContext.SupportTickets
            .AsNoTracking()
            .AsQueryable();

        if (requestedStatus.HasValue)
        {
            tickets = tickets.Where(ticket => ticket.Status == requestedStatus.Value);
        }

        if (normalizedQuery is not null)
        {
            var pattern = $"%{normalizedQuery}%";
            tickets = tickets.Where(ticket =>
                EF.Functions.ILike(ticket.Subject, pattern) ||
                EF.Functions.ILike(ticket.User.Email, pattern) ||
                (ticket.User.FullName != null && EF.Functions.ILike(ticket.User.FullName, pattern)));
        }

        var totalCount = await tickets.CountAsync();
        var ticketRows = await tickets
            .OrderByDescending(ticket => ticket.LastMessageAt)
            .ThenByDescending(ticket => ticket.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(ticket => new
            {
                ticket.Id,
                ticket.Subject,
                ticket.Status,
                ticket.CreatedAt,
                ticket.UpdatedAt,
                ticket.LastMessageAt,
                LastMessageSenderRole = ticket.Messages
                    .OrderByDescending(message => message.CreatedAt)
                    .Select(message => (SupportMessageSenderRole?)message.SenderRole)
                    .FirstOrDefault(),
                ticket.UserId,
                ticket.User.FullName,
                ticket.User.Email
            })
            .ToListAsync();

        var items = ticketRows
            .Select(ticket => new AdminTicketListItemDto(
                ticket.Id,
                ticket.Subject,
                ToApiValue(ticket.Status),
                ticket.CreatedAt,
                ticket.UpdatedAt,
                ticket.LastMessageAt,
                ticket.LastMessageSenderRole is null ? null : ToApiValue(ticket.LastMessageSenderRole.Value),
                ticket.UserId,
                ticket.FullName,
                ticket.Email))
            .ToList();

        return new AdminTicketListResponseDto(
            items,
            normalizedPage,
            normalizedPageSize,
            totalCount,
            CalculateTotalPages(totalCount, normalizedPageSize));
    }

    public async Task<AdminTicketDetailDto> GetTicketDetailAsync(Guid ticketId)
    {
        var ticket = await _dbContext.SupportTickets
            .AsNoTracking()
            .Include(item => item.User)
            .Include(item => item.Messages)
                .ThenInclude(message => message.Sender)
            .FirstOrDefaultAsync(item => item.Id == ticketId);

        return ticket is null
            ? throw new NotFoundException("Destek talebi bulunamadi.")
            : MapToAdminDetail(ticket);
    }

    public async Task<AdminTicketMessageDto> AddAdminReplyAsync(Guid adminUserId, Guid ticketId, AdminReplyDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var messageText = PlainTextInputValidator.Require(dto.Message, "Admin cevabi", MaximumMessageLength);
        var admin = await EnsureActiveAdminAsync(adminUserId);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        var ticket = await GetTicketForUpdateAsync(ticketId);

        if (ticket.Status == SupportTicketStatus.Closed)
        {
            throw new ConflictException("Kapali destek talebine cevap vermek icin once ticket'i yeniden acin.");
        }

        var now = DateTime.UtcNow;
        var message = new SupportTicketMessage
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            SenderId = adminUserId,
            SenderRole = SupportMessageSenderRole.Admin,
            Message = messageText,
            CreatedAt = now
        };

        ticket.Status = SupportTicketStatus.Answered;
        ticket.LastMessageAt = now;
        ticket.UpdatedAt = now;
        _dbContext.SupportTicketMessages.Add(message);

        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        return new AdminTicketMessageDto(
            message.Id,
            message.SenderId,
            ToApiValue(message.SenderRole),
            admin.FullName,
            admin.Email,
            message.Message,
            message.CreatedAt);
    }

    public async Task<AdminTicketDetailDto> UpdateStatusAsync(Guid adminUserId, Guid ticketId, UpdateTicketStatusDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var targetStatus = ParseRequiredStatus(dto.Status);
        await EnsureActiveAdminAsync(adminUserId);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        var ticket = await GetTicketForUpdateAsync(ticketId);

        if (ticket.Status != targetStatus)
        {
            var now = DateTime.UtcNow;
            ticket.Status = targetStatus;
            ticket.UpdatedAt = now;

            if (targetStatus == SupportTicketStatus.Closed)
            {
                ticket.ClosedAt = now;
                ticket.ClosedByUserId = adminUserId;
            }
            else
            {
                ticket.ClosedAt = null;
                ticket.ClosedByUserId = null;
            }

            await _dbContext.SaveChangesAsync();
        }

        await transaction.CommitAsync();
        return await GetTicketDetailAsync(ticketId);
    }

    private async Task<User> EnsureActiveUserAsync(Guid userId)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(item =>
                item.Id == userId &&
                item.IsActive == true &&
                item.DeletedAt == null);

        return user ?? throw new UnauthorizedException("Gecerli aktif kullanici bulunamadi.");
    }

    private async Task<User> EnsureActiveAdminAsync(Guid userId)
    {
        var admin = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(item =>
                item.Id == userId &&
                item.IsActive == true &&
                item.DeletedAt == null &&
                item.Role == UserRole.Admin);

        return admin ?? throw new ForbiddenException("Bu islem icin admin yetkisi gerekir.");
    }

    private async Task<SupportTicket> GetTicketForUpdateAsync(Guid ticketId)
    {
        var ticket = await _dbContext.SupportTickets
            .FromSqlInterpolated($"SELECT * FROM support_tickets WHERE id = {ticketId} FOR UPDATE")
            .SingleOrDefaultAsync();

        return ticket ?? throw new NotFoundException("Destek talebi bulunamadi.");
    }

    private static TicketDetailDto MapToUserDetail(SupportTicket ticket)
    {
        var messages = ticket.Messages
            .OrderBy(message => message.CreatedAt)
            .Select(message => new TicketMessageDto(
                message.Id,
                ToApiValue(message.SenderRole),
                message.Sender.FullName,
                message.Message,
                message.CreatedAt))
            .ToList();

        return new TicketDetailDto(
            ticket.Id,
            ticket.Subject,
            ToApiValue(ticket.Status),
            ticket.CreatedAt,
            ticket.UpdatedAt,
            ticket.LastMessageAt,
            ticket.ClosedAt,
            messages);
    }

    private static AdminTicketDetailDto MapToAdminDetail(SupportTicket ticket)
    {
        var messages = ticket.Messages
            .OrderBy(message => message.CreatedAt)
            .Select(message => new AdminTicketMessageDto(
                message.Id,
                message.SenderId,
                ToApiValue(message.SenderRole),
                message.Sender.FullName,
                message.Sender.Email,
                message.Message,
                message.CreatedAt))
            .ToList();

        return new AdminTicketDetailDto(
            ticket.Id,
            ticket.Subject,
            ToApiValue(ticket.Status),
            ticket.CreatedAt,
            ticket.UpdatedAt,
            ticket.LastMessageAt,
            ticket.ClosedAt,
            ticket.ClosedByUserId,
            ticket.UserId,
            ticket.User.FullName,
            ticket.User.Email,
            messages);
    }

    private static (int Page, int PageSize) NormalizePagination(int page, int pageSize)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = pageSize <= 0
            ? DefaultPageSize
            : Math.Min(pageSize, MaximumPageSize);

        return (normalizedPage, normalizedPageSize);
    }

    private static int CalculateTotalPages(int totalCount, int pageSize)
    {
        return totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)pageSize);
    }

    private static string? NormalizeSearchQuery(string? query)
    {
        return string.IsNullOrWhiteSpace(query)
            ? null
            : PlainTextInputValidator.Require(query, "Arama metni", MaximumSearchLength);
    }

    private static SupportTicketStatus? ParseOptionalStatus(string? status)
    {
        return string.IsNullOrWhiteSpace(status)
            ? null
            : ParseRequiredStatus(status);
    }

    private static SupportTicketStatus ParseRequiredStatus(string status)
    {
        return status.Trim().ToLowerInvariant() switch
        {
            "open" => SupportTicketStatus.Open,
            "answered" => SupportTicketStatus.Answered,
            "closed" => SupportTicketStatus.Closed,
            _ => throw new BadRequestException("Gecersiz ticket durumu.")
        };
    }

    private static string ToApiValue(SupportTicketStatus status)
    {
        return status switch
        {
            SupportTicketStatus.Open => "open",
            SupportTicketStatus.Answered => "answered",
            SupportTicketStatus.Closed => "closed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
    }

    private static string ToApiValue(SupportMessageSenderRole senderRole)
    {
        return senderRole switch
        {
            SupportMessageSenderRole.User => "user",
            SupportMessageSenderRole.Admin => "admin",
            _ => throw new ArgumentOutOfRangeException(nameof(senderRole), senderRole, null)
        };
    }
}
