using System.Text.Json;
using CraftoraApi.Data;
using CraftoraApi.DTOs.Notification;
using CraftoraApi.DTOs.Support;
using CraftoraApi.Infrastructure.Security;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CraftoraApi.Services;

public sealed class SupportTicketService : ISupportTicketService
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;
    private const int MaximumSearchLength = 100;
    private const int MaximumSubjectLength = 200;
    private const int MaximumMessageLength = 5000;
    private const string SupportTicketReferenceType = "support_ticket";
    private const string SupportTicketAuditTargetType = "support_ticket";

    private readonly AppDbContext _dbContext;
    private readonly INotificationService _notificationService;
    private readonly ILogger<SupportTicketService> _logger;

    public SupportTicketService(
        AppDbContext dbContext,
        INotificationService notificationService,
        ILogger<SupportTicketService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        var adminIds = await _dbContext.Users
            .AsNoTracking()
            .Where(item => item.Role == UserRole.Admin && item.IsActive == true && item.DeletedAt == null)
            .Select(item => item.Id)
            .ToListAsync();

        foreach (var adminId in adminIds)
        {
            await TrySendSupportNotificationAsync(
                adminId,
                ticket.Id,
                "Yeni destek talebi",
                "Yeni bir destek talebi olusturuldu.");
        }

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
                    .FirstOrDefault(),
                LastMessagePreview = ticket.Messages
                    .OrderByDescending(message => message.CreatedAt)
                    .Select(message => message.Message)
                    .FirstOrDefault()
            })
            .ToListAsync();

        var ticketIds = ticketRows.Select(ticket => ticket.Id).ToList();
        var messageRows = ticketIds.Count == 0
            ? new List<(Guid TicketId, SupportMessageSenderRole SenderRole, DateTime CreatedAt)>()
            : (await _dbContext.SupportTicketMessages
                .AsNoTracking()
                .Where(message => ticketIds.Contains(message.TicketId))
                .Select(message => new { message.TicketId, message.SenderRole, message.CreatedAt })
                .ToListAsync())
                .Select(message => (message.TicketId, message.SenderRole, message.CreatedAt))
                .ToList();

        var items = ticketRows
            .Select(ticket =>
            {
                var ticketMessages = messageRows.Where(message => message.TicketId == ticket.Id).ToList();
                var lastUserMessageAt = ticketMessages
                    .Where(message => message.SenderRole == SupportMessageSenderRole.User)
                    .Select(message => (DateTime?)message.CreatedAt)
                    .Max();
                var unreadCount = ticketMessages.Count(message =>
                    message.SenderRole == SupportMessageSenderRole.Admin &&
                    (!lastUserMessageAt.HasValue || message.CreatedAt > lastUserMessageAt.Value));

                return new TicketListItemDto(
                    ticket.Id,
                    ticket.Subject,
                    ToApiValue(ticket.Status),
                    ticket.CreatedAt,
                    ticket.UpdatedAt,
                    ticket.LastMessageAt,
                    ticket.LastMessageSenderRole is null ? null : ToApiValue(ticket.LastMessageSenderRole.Value),
                    ticket.LastMessagePreview,
                    unreadCount);
            })
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
        var requester = await EnsureActiveUserAsync(userId);

        var ticketQuery = _dbContext.SupportTickets
            .AsNoTracking()
            .Include(item => item.Messages)
                .ThenInclude(message => message.Sender)
            .AsQueryable();

        if (requester.Role != UserRole.Admin)
        {
            ticketQuery = ticketQuery.Where(item => item.UserId == userId);
        }

        var ticket = await ticketQuery.FirstOrDefaultAsync(item => item.Id == ticketId);

        return ticket is null
            ? throw new NotFoundException("Destek talebi bulunamadi.")
            : MapToUserDetail(ticket);
    }

    public async Task<SupportMessageResponseDto> AddMessageAsync(Guid userId, Guid ticketId, AddMessageDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var messageText = PlainTextInputValidator.Require(dto.Message, "Ticket mesaji", MaximumMessageLength);
        var user = await EnsureActiveUserAsync(userId);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        var ticket = await GetTicketForUpdateAsync(ticketId);

        var isAdmin = user.Role == UserRole.Admin;
        if (ticket.UserId != userId && !isAdmin)
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
            SenderRole = isAdmin ? SupportMessageSenderRole.Admin : SupportMessageSenderRole.User,
            Message = messageText,
            CreatedAt = now
        };

        ticket.Status = isAdmin ? SupportTicketStatus.Answered : SupportTicketStatus.Open;
        ticket.LastMessageAt = now;
        ticket.UpdatedAt = now;
        _dbContext.SupportTicketMessages.Add(message);

        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        if (isAdmin)
        {
            await TrySendSupportNotificationAsync(
                ticket.UserId,
                ticketId,
                "Destek talebinize yanit verildi",
                "Destek talebinize bir admin yaniti eklendi.");
        }

        return new SupportMessageResponseDto(new TicketMessageDto(
            message.Id,
            message.SenderId,
            ToApiValue(message.SenderRole, user.Role),
            user.FullName,
            message.Message,
            message.CreatedAt),
            ToApiValue(ticket.Status));
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

        AdminTicketMessageDto result;
        Guid ticketOwnerId;

        await using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
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
            await AddAdminAuditAsync(
                adminUserId,
                "support_reply",
                ticket.Id,
                new { MessageLength = messageText.Length, Status = ToApiValue(ticket.Status) });
            await transaction.CommitAsync();

            ticketOwnerId = ticket.UserId;
            result = new AdminTicketMessageDto(
                message.Id,
                message.SenderId,
                ToApiValue(message.SenderRole),
                admin.FullName,
                admin.Email,
                message.Message,
                message.CreatedAt);
        }

        await TrySendSupportNotificationAsync(
            ticketOwnerId,
            ticketId,
            "Destek talebinize yanit verildi",
            "Destek talebinize bir admin yaniti eklendi.");

        return result;
    }

    public async Task<AdminTicketDetailDto> UpdateStatusAsync(Guid adminUserId, Guid ticketId, UpdateTicketStatusDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var targetStatus = ParseRequiredStatus(dto.Status);
        await EnsureActiveAdminAsync(adminUserId);

        var shouldNotifyClosure = false;
        Guid ticketOwnerId;

        await using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            var ticket = await GetTicketForUpdateAsync(ticketId);
            ticketOwnerId = ticket.UserId;
            var previousStatus = ticket.Status;

            if (previousStatus != targetStatus)
            {
                var now = DateTime.UtcNow;
                ticket.Status = targetStatus;
                ticket.UpdatedAt = now;

                if (targetStatus == SupportTicketStatus.Closed)
                {
                    ticket.ClosedAt = now;
                    ticket.ClosedByUserId = adminUserId;
                    shouldNotifyClosure = true;
                }
                else
                {
                    ticket.ClosedAt = null;
                    ticket.ClosedByUserId = null;
                }

                await _dbContext.SaveChangesAsync();
                await AddAdminAuditAsync(
                    adminUserId,
                    "support_status_change",
                    ticket.Id,
                    new
                    {
                        PreviousStatus = ToApiValue(previousStatus),
                        Status = ToApiValue(targetStatus)
                    });
            }

            await transaction.CommitAsync();
        }

        if (shouldNotifyClosure)
        {
            await TrySendSupportNotificationAsync(
                ticketOwnerId,
                ticketId,
                "Destek talebiniz kapatildi",
                "Destek talebiniz kapatildi. Gerekirse yeni bir destek talebi olusturabilirsiniz.");
        }

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

    private async Task AddAdminAuditAsync(Guid adminUserId, string action, Guid ticketId, object metadata)
    {
        await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO admin_audit_logs (admin_user_id, action, target_type, target_id, metadata)
            VALUES (
                {adminUserId},
                {action},
                {SupportTicketAuditTargetType},
                {ticketId},
                CAST({JsonSerializer.Serialize(metadata)} AS jsonb)
            )
            """);
    }

    private async Task TrySendSupportNotificationAsync(Guid userId, Guid ticketId, string title, string message)
    {
        try
        {
            await _notificationService.SendNotificationAsync(
                userId,
                title,
                message,
                NotificationType.System,
                ticketId,
                SupportTicketReferenceType);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Support ticket notification could not be sent. TicketId: {TicketId}, UserId: {UserId}",
                ticketId,
                userId);
        }
    }

    private static TicketDetailDto MapToUserDetail(SupportTicket ticket)
    {
        var messages = ticket.Messages
            .OrderBy(message => message.CreatedAt)
            .Select(message => new TicketMessageDto(
                message.Id,
                message.SenderId,
                ToApiValue(message.SenderRole, message.Sender.Role),
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

    private static string ToApiValue(SupportMessageSenderRole senderRole, UserRole senderUserRole)
    {
        if (senderUserRole == UserRole.Admin)
        {
            return "admin";
        }

        return senderUserRole == UserRole.Seller ? "seller" : "user";
    }
}
