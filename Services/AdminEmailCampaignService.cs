using CraftoraApi.Data;
using CraftoraApi.DTOs.Admin;
using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Services.Interfaces;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Services;

public sealed class AdminEmailCampaignService : IAdminEmailCampaignService
{
    private const int MaximumRecipientCount = 50000;

    private readonly AppDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<AdminEmailCampaignService> _logger;

    public AdminEmailCampaignService(
        AppDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        ILogger<AdminEmailCampaignService> logger)
    {
        _dbContext = dbContext;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<AdminEmailCampaignPreviewDto> PreviewAsync(
        AdminEmailCampaignPreviewRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var audience = NormalizeAudience(request.Audience);
        var query = BuildRecipientQuery(audience);
        var count = await query.CountAsync(cancellationToken);
        var sample = await query
            .OrderBy(user => user.CreatedAt)
            .Select(user => user.Email)
            .Take(5)
            .ToListAsync(cancellationToken);

        return new AdminEmailCampaignPreviewDto(
            audience,
            count,
            sample,
            request.Subject.Trim(),
            AdminEmailTemplate.Build("Craftora kullanıcısı", request.Message));
    }

    public async Task<AdminEmailCampaignDto> CreateAndDispatchAsync(
        Guid adminUserId,
        AdminEmailCampaignSendRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var audience = NormalizeAudience(request.Audience);
        var idempotencyKey = request.IdempotencyKey.Trim();

        var existing = await _dbContext.AdminEmailCampaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.AdminUserId == adminUserId &&
                    item.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (existing is not null)
        {
            var pendingRecipientIds = await _dbContext.AdminEmailCampaignRecipients
                .AsNoTracking()
                .Where(item =>
                    item.CampaignId == existing.Id &&
                    item.Status == "pending")
                .Select(item => item.Id)
                .ToListAsync(cancellationToken);
            await PublishRecipientsAsync(pendingRecipientIds, cancellationToken);
            return Map(existing);
        }

        var recipients = await BuildRecipientQuery(audience)
            .OrderBy(user => user.Id)
            .Select(user => new
            {
                user.Id,
                user.Email,
                user.FullName
            })
            .Take(MaximumRecipientCount + 1)
            .ToListAsync(cancellationToken);

        if (recipients.Count == 0)
        {
            throw new BadRequestException("Secilen hedef kitlede uygun alici bulunamadi.");
        }

        if (recipients.Count > MaximumRecipientCount)
        {
            throw new BadRequestException(
                $"Tek kampanyada en fazla {MaximumRecipientCount} aliciya gonderim yapilabilir.");
        }

        var now = DateTime.UtcNow;
        var campaign = new AdminEmailCampaign
        {
            Id = Guid.NewGuid(),
            AdminUserId = adminUserId,
            IdempotencyKey = idempotencyKey,
            Audience = audience,
            Subject = request.Subject.Trim(),
            Message = request.Message.Trim(),
            Status = "queued",
            RecipientCount = recipients.Count,
            CreatedAt = now
        };

        foreach (var recipient in recipients)
        {
            campaign.Recipients.Add(new AdminEmailCampaignRecipient
            {
                Id = Guid.NewGuid(),
                CampaignId = campaign.Id,
                UserId = recipient.Id,
                Email = recipient.Email,
                FullName = recipient.FullName,
                Status = "pending",
                CreatedAt = now
            });
        }

        _dbContext.AdminEmailCampaigns.Add(campaign);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await PublishRecipientsAsync(
            campaign.Recipients.Select(item => item.Id),
            cancellationToken);

        _logger.LogInformation(
            "Admin email campaign queued. CampaignId: {CampaignId}, AdminUserId: {AdminUserId}, Audience: {Audience}, RecipientCount: {RecipientCount}",
            campaign.Id,
            adminUserId,
            audience,
            campaign.RecipientCount);

        return Map(campaign);
    }

    public async Task<AdminEmailCampaignDto> GetAsync(
        Guid adminUserId,
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        var campaign = await FindOwnedCampaignAsync(
            adminUserId,
            campaignId,
            cancellationToken);
        return Map(campaign);
    }

    public async Task<AdminPagedResponseDto<AdminEmailCampaignDto>> GetListAsync(
        Guid adminUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 100)
        {
            throw new BadRequestException("Sayfalama degerleri gecersiz.");
        }

        var query = _dbContext.AdminEmailCampaigns
            .AsNoTracking()
            .Where(item => item.AdminUserId == adminUserId);
        var totalCount = await query.CountAsync(cancellationToken);
        var campaigns = await query
            .OrderByDescending(item => item.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return new AdminPagedResponseDto<AdminEmailCampaignDto>(
            campaigns.Select(Map).ToList(),
            page,
            pageSize,
            totalCount,
            totalPages);
    }

    public async Task<AdminEmailCampaignDto> RetryFailedAsync(
        Guid adminUserId,
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        var campaign = await FindOwnedCampaignAsync(
            adminUserId,
            campaignId,
            cancellationToken);

        var failedRecipients = await _dbContext.AdminEmailCampaignRecipients
            .Where(item => item.CampaignId == campaignId && item.Status == "failed")
            .ToListAsync(cancellationToken);
        if (failedRecipients.Count == 0)
        {
            throw new ConflictException("Yeniden denenecek basarisiz alici bulunamadi.");
        }

        foreach (var recipient in failedRecipients)
        {
            recipient.Status = "pending";
            recipient.ErrorMessage = null;
        }

        campaign.Status = "queued";
        campaign.CompletedAt = null;
        campaign.FailedCount = Math.Max(0, campaign.FailedCount - failedRecipients.Count);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await PublishRecipientsAsync(
            failedRecipients.Select(item => item.Id),
            cancellationToken);

        return Map(campaign);
    }

    private IQueryable<User> BuildRecipientQuery(string audience)
    {
        var query = _dbContext.Users
            .AsNoTracking()
            .Where(user =>
                user.IsActive == true &&
                user.IsEmailVerified == true &&
                user.DeletedAt == null &&
                user.Role != UserRole.Admin);

        return audience switch
        {
            "users" => query.Where(user => user.Role == UserRole.User),
            "sellers" => query.Where(user => user.Role == UserRole.Seller),
            _ => query
        };
    }

    private async Task<AdminEmailCampaign> FindOwnedCampaignAsync(
        Guid adminUserId,
        Guid campaignId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.AdminEmailCampaigns
            .FirstOrDefaultAsync(
                item => item.Id == campaignId && item.AdminUserId == adminUserId,
                cancellationToken)
            ?? throw new NotFoundException("E-posta kampanyasi bulunamadi.");
    }

    private async Task PublishRecipientsAsync(
        IEnumerable<Guid> recipientIds,
        CancellationToken cancellationToken)
    {
        foreach (var recipientId in recipientIds)
        {
            await _publishEndpoint.Publish(
                new SendAdminCampaignEmailCommand(recipientId),
                cancellationToken);
        }
    }

    private static string NormalizeAudience(string audience)
    {
        var normalized = audience.Trim().ToLowerInvariant();
        return normalized is "all" or "users" or "sellers"
            ? normalized
            : throw new BadRequestException("Gecersiz hedef kitle.");
    }

    private static AdminEmailCampaignDto Map(AdminEmailCampaign campaign)
    {
        return new AdminEmailCampaignDto(
            campaign.Id,
            campaign.Audience,
            campaign.Subject,
            campaign.Status,
            campaign.RecipientCount,
            campaign.SentCount,
            campaign.FailedCount,
            campaign.CreatedAt,
            campaign.StartedAt,
            campaign.CompletedAt);
    }
}
