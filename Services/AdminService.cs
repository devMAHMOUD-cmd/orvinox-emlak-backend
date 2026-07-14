using System.Data;
using System.Data.Common;
using System.Text.Json;
using CraftoraApi.Data;
using CraftoraApi.DTOs.Admin;
using CraftoraApi.DTOs.Gamification;
using CraftoraApi.DTOs.Notification;
using CraftoraApi.Infrastructure.Security;
using CraftoraApi.Middleware;
using CraftoraApi.Models.Entities;
using CraftoraApi.Models.Enums;
using CraftoraApi.Redis;
using CraftoraApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CraftoraApi.Services;

public sealed class AdminService : IAdminService
{
    private const string ProductReindexLockKey = "search:reindex:products";
    private static readonly TimeSpan ProductReindexLockTtl = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _dbContext;
    private readonly INotificationService _notificationService;
    private readonly IGamificationService _gamificationService;
    private readonly ISearchService _searchService;
    private readonly ICacheService _cacheService;

    public AdminService(
        AppDbContext dbContext,
        INotificationService notificationService,
        IGamificationService gamificationService,
        ISearchService searchService,
        ICacheService cacheService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _gamificationService = gamificationService ?? throw new ArgumentNullException(nameof(gamificationService));
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
    }

    public async Task<AdminOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.Date;

        return new AdminOverviewDto(
            TotalUsers: await _dbContext.Users.CountAsync(cancellationToken),
            TotalSellers: await _dbContext.Users.CountAsync(user => user.Role == UserRole.Seller, cancellationToken),
            TotalShops: await _dbContext.Shops.CountAsync(cancellationToken),
            TotalProducts: await _dbContext.Products.CountAsync(cancellationToken),
            TotalCourses: await _dbContext.Courses.CountAsync(cancellationToken),
            TotalMedia: await _dbContext.Media.CountAsync(cancellationToken),
            TotalOrders: await _dbContext.Orders.CountAsync(cancellationToken),
            GrossRevenue: await _dbContext.Orders
                .Where(order => order.Status == OrderStatus.Completed)
                .SumAsync(order => order.Amount, cancellationToken),
            PendingReports: await CountRawAsync(
                "SELECT COUNT(*) FROM admin_reports WHERE status IN ('open', 'reviewing')",
                cancellationToken),
            ActiveCompetitions: await _dbContext.Contests.CountAsync(
                contest => contest.IsActive == true && contest.StartDate <= DateTime.UtcNow && contest.EndDate >= DateTime.UtcNow,
                cancellationToken),
            NewUsersToday: await _dbContext.Users.CountAsync(user => user.CreatedAt >= today, cancellationToken),
            OrdersToday: await _dbContext.Orders.CountAsync(order => order.CreatedAt >= today, cancellationToken));
    }

    public async Task<int> ReindexProductsAsync(
        Guid adminUserId,
        CancellationToken cancellationToken = default)
    {
        var lockValue = Guid.NewGuid().ToString("N");
        bool lockAcquired;
        try
        {
            lockAcquired = await _cacheService.TryAcquireLockAsync(
                ProductReindexLockKey,
                lockValue,
                ProductReindexLockTtl);
        }
        catch
        {
            throw new ExternalServiceException("Redis", "Reindex kilidi alinamadi. Lutfen tekrar deneyin.");
        }

        if (!lockAcquired)
        {
            throw new ConflictException("Urun reindex islemi zaten devam ediyor.");
        }

        try
        {
            var indexedCount = await _searchService.ReindexProductsAsync(cancellationToken);
            await AddAuditAsync(
                adminUserId,
                "reindex_products",
                "search_index",
                null,
                new { indexedCount },
                cancellationToken);

            return indexedCount;
        }
        finally
        {
            try
            {
                await _cacheService.ReleaseLockAsync(ProductReindexLockKey, lockValue);
            }
            catch
            {
                // The lock expires automatically; a release failure must not hide a successful reindex.
            }
        }
    }

    public async Task<AdminPagedResponseDto<AdminUserListItemDto>> GetUsersAsync(
        string? query,
        string? role,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var usersQuery = _dbContext.Users
            .AsNoTracking()
            .Include(user => user.Shop)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{query.Trim()}%";
            usersQuery = usersQuery.Where(user =>
                EF.Functions.ILike(user.Email, pattern) ||
                (user.FullName != null && EF.Functions.ILike(user.FullName, pattern)) ||
                (user.Shop != null && EF.Functions.ILike(user.Shop.ShopName, pattern)));
        }

        if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<UserRole>(role, ignoreCase: true, out var parsedRole))
        {
            usersQuery = usersQuery.Where(user => user.Role == parsedRole);
        }

        usersQuery = ApplyUserStatusFilter(usersQuery, status);

        var totalCount = await usersQuery.CountAsync(cancellationToken);
        var users = await usersQuery
            .OrderByDescending(user => user.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        var userIds = users.Select(user => user.Id).ToList();
        var shopIds = users.Select(user => user.Shop?.Id).Where(id => id.HasValue).Select(id => id!.Value).ToList();
        var productCounts = await BuildProductCountsAsync(shopIds, ProductType.DigitalFile, cancellationToken);
        var courseCounts = await BuildProductCountsAsync(shopIds, ProductType.Course, cancellationToken);
        var mediaCounts = await _dbContext.Media
            .AsNoTracking()
            .Where(media => shopIds.Contains(media.ShopId))
            .GroupBy(media => media.ShopId)
            .Select(group => new { ShopId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.ShopId, item => item.Count, cancellationToken);
        var orderCounts = await _dbContext.Orders
            .AsNoTracking()
            .Where(order => userIds.Contains(order.BuyerId))
            .GroupBy(order => order.BuyerId)
            .Select(group => new { UserId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.UserId, item => item.Count, cancellationToken);
        var totalXp = await _dbContext.UserPoints
            .AsNoTracking()
            .Where(point => userIds.Contains(point.UserId))
            .ToDictionaryAsync(point => point.UserId, point => point.TotalPoints ?? 0, cancellationToken);
        var reportCounts = await GetReportCountsByUserAsync(userIds, cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);

        return new AdminPagedResponseDto<AdminUserListItemDto>(
            Items: users.Select(user => MapUserListItem(
                user,
                productCounts,
                courseCounts,
                mediaCounts,
                orderCounts,
                reportCounts,
                totalXp)).ToList(),
            Page: normalizedPage,
            PageSize: normalizedPageSize,
            TotalCount: totalCount,
            TotalPages: totalPages);
    }

    public async Task<AdminUserDetailDto> GetUserDetailAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(item => item.Shop)
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user is null)
        {
            throw new NotFoundException("Kullanici bulunamadi.");
        }

        var shopId = user.Shop?.Id;
        var productCounts = await BuildProductCountsAsync(shopId.HasValue ? new List<Guid> { shopId.Value } : new List<Guid>(), ProductType.DigitalFile, cancellationToken);
        var courseCounts = await BuildProductCountsAsync(shopId.HasValue ? new List<Guid> { shopId.Value } : new List<Guid>(), ProductType.Course, cancellationToken);
        var mediaCounts = shopId.HasValue
            ? new Dictionary<Guid, int>
            {
                [shopId.Value] = await _dbContext.Media.CountAsync(media => media.ShopId == shopId.Value, cancellationToken)
            }
            : new Dictionary<Guid, int>();
        var orderCounts = new Dictionary<Guid, int>
        {
            [userId] = await _dbContext.Orders.CountAsync(order => order.BuyerId == userId, cancellationToken)
        };
        var reportCounts = await GetReportCountsByUserAsync(new List<Guid> { userId }, cancellationToken);
        var xp = await _dbContext.UserPoints
            .AsNoTracking()
            .Where(point => point.UserId == userId)
            .ToDictionaryAsync(point => point.UserId, point => point.TotalPoints ?? 0, cancellationToken);
        var products = shopId.HasValue
            ? await GetProductSummariesAsync(shopId.Value, ProductType.DigitalFile, cancellationToken)
            : new List<AdminProductSummaryDto>();
        var courses = shopId.HasValue
            ? await GetProductSummariesAsync(shopId.Value, ProductType.Course, cancellationToken)
            : new List<AdminProductSummaryDto>();
        var media = shopId.HasValue
            ? await GetMediaSummariesAsync(shopId.Value, cancellationToken)
            : new List<AdminMediaSummaryDto>();
        var orders = await _dbContext.Orders
            .AsNoTracking()
            .Where(order => order.BuyerId == userId || (shopId.HasValue && order.ShopId == shopId.Value))
            .OrderByDescending(order => order.CreatedAt)
            .Take(50)
            .Select(order => new AdminOrderSummaryDto(
                order.Id,
                order.OrderNumber,
                order.Amount,
                order.Currency,
                order.Status.ToString(),
                order.CreatedAt))
            .ToListAsync(cancellationToken);
        var reports = await GetReportsForUserAsync(userId, cancellationToken);
        var warnings = await GetWarningsForUserAsync(userId, cancellationToken);
        var gamification = await _gamificationService.GetProfileAsync(userId);

        return new AdminUserDetailDto(
            User: MapUserListItem(user, productCounts, courseCounts, mediaCounts, orderCounts, reportCounts, xp),
            Shop: user.Shop is null
                ? null
                : new AdminShopSummaryDto(
                    user.Shop.Id,
                    user.Shop.ShopName,
                    user.Shop.Slug,
                    user.Shop.IsActive == true,
                    user.Shop.IsVerified == true,
                    user.Shop.CreatedAt),
            Products: products,
            Courses: courses,
            Media: media,
            Orders: orders,
            Reports: reports,
            Warnings: warnings,
            Gamification: gamification);
    }

    public async Task WarnUserAsync(Guid adminUserId, Guid userId, AdminWarnUserRequestDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var title = PlainTextInputValidator.Require(dto.Title, "Uyari basligi", 200);
        var message = PlainTextInputValidator.Require(dto.Message, "Uyari mesaji", 1000);
        await EnsureUserExistsAsync(userId, cancellationToken);
        await ExecuteAsync(
            "INSERT INTO admin_warnings (user_id, admin_user_id, title, message) VALUES (@p0, @p1, @p2, @p3)",
            cancellationToken,
            userId,
            adminUserId,
            title,
            message);
        await _notificationService.SendNotificationAsync(userId, title, message, NotificationType.System, null);
        await AddAuditAsync(adminUserId, "warn_user", "user", userId, new { Title = title, Message = message }, cancellationToken);
    }

    public async Task LockUserAsync(Guid adminUserId, Guid userId, AdminLockUserRequestDto dto, CancellationToken cancellationToken = default)
    {
        var user = await GetUserForUpdateAsync(userId, cancellationToken);
        await EnsureUserCanBeRestrictedAsync(
            adminUserId,
            user,
            "Kendi hesabinizi kilitleyemezsiniz.",
            cancellationToken);

        user.LockedUntil = dto.Until;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync(adminUserId, "lock_user", "user", userId, new { dto.Reason, dto.Until }, cancellationToken);
    }

    public async Task UnlockUserAsync(Guid adminUserId, Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await GetUserForUpdateAsync(userId, cancellationToken);
        user.LockedUntil = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync(adminUserId, "unlock_user", "user", userId, new { }, cancellationToken);
    }

    public async Task SuspendUserAsync(Guid adminUserId, Guid userId, AdminSuspendUserRequestDto dto, CancellationToken cancellationToken = default)
    {
        var user = await GetUserForUpdateAsync(userId, cancellationToken);
        await EnsureUserCanBeRestrictedAsync(
            adminUserId,
            user,
            "Kendi hesabinizi askiya alamazsiniz.",
            cancellationToken);

        user.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync(adminUserId, "suspend_user", "user", userId, new { dto.Reason }, cancellationToken);
    }

    public async Task RestoreUserAsync(Guid adminUserId, Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await GetUserForUpdateAsync(userId, cancellationToken);
        user.IsActive = true;
        user.DeletedAt = null;
        user.LockedUntil = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync(adminUserId, "restore_user", "user", userId, new { }, cancellationToken);
    }

    public async Task SoftDeleteUserAsync(Guid adminUserId, Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await GetUserForUpdateAsync(userId, cancellationToken);
        await EnsureUserCanBeRestrictedAsync(
            adminUserId,
            user,
            "Kendi hesabinizi silemezsiniz.",
            cancellationToken);

        user.IsActive = false;
        user.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync(adminUserId, "delete_user", "user", userId, new { softDelete = true }, cancellationToken);
    }

    public async Task<AdminPagedResponseDto<AdminReportDto>> GetReportsAsync(
        string? status,
        string? type,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var filters = new List<string>();
        var parameters = new List<object?>();

        if (!string.IsNullOrWhiteSpace(status))
        {
            filters.Add("r.status = @p" + parameters.Count);
            parameters.Add(status.Trim());
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            filters.Add("r.type = @p" + parameters.Count);
            parameters.Add(type.Trim());
        }

        var where = filters.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", filters);
        var totalCount = await CountRawAsync($"SELECT COUNT(*) FROM admin_reports r {where}", cancellationToken, parameters.ToArray());
        parameters.Add((normalizedPage - 1) * normalizedPageSize);
        parameters.Add(normalizedPageSize);
        var items = await QueryReportsAsync(
            $"""
            SELECT r.id, r.type, r.target_id, r.target_title, r.reported_by_user_id, u.email, r.reason, r.description, r.status, r.created_at
            FROM admin_reports r
            LEFT JOIN users u ON u.id = r.reported_by_user_id
            {where}
            ORDER BY r.created_at DESC
            OFFSET @p{parameters.Count - 2} LIMIT @p{parameters.Count - 1}
            """,
            parameters.ToArray(),
            cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);

        return new AdminPagedResponseDto<AdminReportDto>(items, normalizedPage, normalizedPageSize, totalCount, totalPages);
    }

    public async Task ResolveReportAsync(Guid adminUserId, Guid reportId, CancellationToken cancellationToken = default)
    {
        await UpdateReportStatusAsync(adminUserId, reportId, "resolved", cancellationToken);
    }

    public async Task RejectReportAsync(Guid adminUserId, Guid reportId, CancellationToken cancellationToken = default)
    {
        await UpdateReportStatusAsync(adminUserId, reportId, "rejected", cancellationToken);
    }

    public async Task<IReadOnlyList<AdminCompetitionDto>> GetCompetitionsAsync(CancellationToken cancellationToken = default)
    {
        var contests = await _dbContext.Contests
            .AsNoTracking()
            .OrderByDescending(contest => contest.StartDate)
            .ToListAsync(cancellationToken);

        return contests.Select(MapCompetition).ToList();
    }

    public async Task<AdminCompetitionDto> CreateCompetitionAsync(Guid adminUserId, AdminUpsertCompetitionDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ValidateCompetitionDateRange(dto);

        var contest = new Contest
        {
            Title = dto.Title.Trim(),
            Description = dto.Description,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            RewardsHidden = dto.RewardsHidden,
            PrizePool = dto.PrizePool,
            IsActive = IsActiveCompetitionStatus(dto.Status),
            CreatedBy = adminUserId
        };

        _dbContext.Contests.Add(contest);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync(adminUserId, "create_competition", "competition", contest.Id, dto, cancellationToken);

        return MapCompetition(contest);
    }

    public async Task<AdminCompetitionDto> UpdateCompetitionAsync(Guid adminUserId, Guid id, AdminUpsertCompetitionDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ValidateCompetitionDateRange(dto);

        var contest = await GetContestForUpdateAsync(id, cancellationToken);
        contest.Title = dto.Title.Trim();
        contest.Description = dto.Description;
        contest.StartDate = dto.StartDate;
        contest.EndDate = dto.EndDate;
        contest.RewardsHidden = dto.RewardsHidden;
        contest.PrizePool = dto.PrizePool;
        contest.IsActive = IsActiveCompetitionStatus(dto.Status);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync(adminUserId, "update_competition", "competition", id, dto, cancellationToken);

        return MapCompetition(contest);
    }

    public async Task StartCompetitionAsync(Guid adminUserId, Guid id, CancellationToken cancellationToken = default)
    {
        var contest = await GetContestForUpdateAsync(id, cancellationToken);
        contest.IsActive = true;
        if (contest.StartDate > DateTime.UtcNow)
        {
            contest.StartDate = DateTime.UtcNow;
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync(adminUserId, "start_competition", "competition", id, new { }, cancellationToken);
    }

    public async Task FinishCompetitionAsync(Guid adminUserId, Guid id, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var contest = await GetContestForUpdateWithLockAsync(id, cancellationToken);
        if (IsFinishedCompetition(contest))
        {
            throw new BadRequestException("Bu yarisma zaten sonuclandirilmis.");
        }

        contest.IsActive = false;
        contest.EndDate = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync(adminUserId, "finish_competition", "competition", id, new { }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DistributeRewardsAsync(Guid adminUserId, Guid id, AdminDistributeRewardsRequestDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Winners.Count == 0)
        {
            throw new BadRequestException("Odul dagitimi icin en az bir kazanan gereklidir.");
        }

        if (dto.Winners.GroupBy(winner => winner.UserId).Any(group => group.Count() > 1))
        {
            throw new BadRequestException("Ayni kullaniciya bir yarismada birden fazla odul verilemez.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var contest = await GetContestForUpdateWithLockAsync(id, cancellationToken);
        var existingRewardCount = await CountRawAsync(
            "SELECT COUNT(*) FROM admin_competition_rewards WHERE contest_id = @p0",
            cancellationToken,
            id);
        if (existingRewardCount > 0)
        {
            throw new BadRequestException("Bu yarisma zaten sonuclandirilmis.");
        }

        contest.IsActive = false;
        if (contest.EndDate > DateTime.UtcNow)
        {
            contest.EndDate = DateTime.UtcNow;
        }

        foreach (var winner in dto.Winners)
        {
            await ExecuteAsync(
                """
                INSERT INTO admin_competition_rewards (contest_id, user_id, rank, reward_type, amount, currency, note)
                VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6)
                """,
                cancellationToken,
                id,
                winner.UserId,
                winner.Rank,
                winner.RewardType,
                winner.Amount,
                winner.Currency,
                winner.Note);

            var result = await _dbContext.ContestResults.FirstOrDefaultAsync(
                item => item.ContestId == id && item.UserId == winner.UserId,
                cancellationToken);
            if (result is null)
            {
                _dbContext.ContestResults.Add(new ContestResult
                {
                    ContestId = id,
                    UserId = winner.UserId,
                    FinalRank = winner.Rank,
                    RewardClaimed = false,
                    JoinedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                result.FinalRank = winner.Rank;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync(adminUserId, "distribute_rewards", "competition", id, dto, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<IReadOnlyList<PulseNewsDto>> GetPulseNewsAsync(bool includeUnpublished, CancellationToken cancellationToken = default)
    {
        var where = includeUnpublished ? string.Empty : "WHERE is_published = true";
        return QueryPulseNewsAsync(
            $"SELECT id, title, description, meta, icon, is_published, is_new_until, created_at, updated_at FROM pulse_news {where} ORDER BY created_at DESC",
            Array.Empty<object?>(),
            cancellationToken);
    }

    public async Task<PulseNewsDto> CreatePulseNewsAsync(Guid adminUserId, UpsertPulseNewsDto dto, CancellationToken cancellationToken = default)
    {
        var title = PlainTextInputValidator.Require(dto.Title, "Pulse haber basligi", 200);
        var description = PlainTextInputValidator.Optional(dto.Description, "Pulse haber aciklamasi", 1000);
        var meta = PlainTextInputValidator.Optional(dto.Meta, "Pulse haber meta bilgisi", 300);
        var id = Guid.NewGuid();
        await ExecuteAsync(
            """
            INSERT INTO pulse_news (id, title, description, meta, icon, is_published, is_new_until)
            VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6)
            """,
            cancellationToken,
            id,
            title,
            description,
            meta,
            dto.Icon,
            dto.IsPublished,
            dto.IsNewUntil);
        await AddAuditAsync(adminUserId, "create_pulse_news", "pulse_news", id, new { Title = title, Description = description, Meta = meta, dto.Icon, dto.IsPublished, dto.IsNewUntil }, cancellationToken);
        if (dto.IsPublished)
        {
            await SendSystemNotificationToAllUsersAsync(title, description ?? title, id, cancellationToken);
        }

        return (await QueryPulseNewsAsync(
            "SELECT id, title, description, meta, icon, is_published, is_new_until, created_at, updated_at FROM pulse_news WHERE id = @p0",
            new object?[] { id },
            cancellationToken)).First();
    }

    public async Task<PulseNewsDto> UpdatePulseNewsAsync(Guid adminUserId, Guid id, UpsertPulseNewsDto dto, CancellationToken cancellationToken = default)
    {
        var title = PlainTextInputValidator.Require(dto.Title, "Pulse haber basligi", 200);
        var description = PlainTextInputValidator.Optional(dto.Description, "Pulse haber aciklamasi", 1000);
        var meta = PlainTextInputValidator.Optional(dto.Meta, "Pulse haber meta bilgisi", 300);
        await ExecuteAsync(
            """
            UPDATE pulse_news
            SET title = @p1, description = @p2, meta = @p3, icon = @p4, is_published = @p5, is_new_until = @p6, updated_at = CURRENT_TIMESTAMP
            WHERE id = @p0
            """,
            cancellationToken,
            id,
            title,
            description,
            meta,
            dto.Icon,
            dto.IsPublished,
            dto.IsNewUntil);
        await AddAuditAsync(adminUserId, "update_pulse_news", "pulse_news", id, new { Title = title, Description = description, Meta = meta, dto.Icon, dto.IsPublished, dto.IsNewUntil }, cancellationToken);

        return (await QueryPulseNewsAsync(
            "SELECT id, title, description, meta, icon, is_published, is_new_until, created_at, updated_at FROM pulse_news WHERE id = @p0",
            new object?[] { id },
            cancellationToken)).First();
    }

    public async Task DeletePulseNewsAsync(Guid adminUserId, Guid id, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync("DELETE FROM pulse_news WHERE id = @p0", cancellationToken, id);
        await AddAuditAsync(adminUserId, "delete_pulse_news", "pulse_news", id, new { }, cancellationToken);
    }

    public Task<HomeCardsDto> GetHomeCardsAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var where = includeInactive ? string.Empty : "WHERE is_active = true";
        return QueryHomeCardsAsync(
            $"SELECT id, title, description, icon, action_type, sort_order, is_active FROM home_cards {where} ORDER BY sort_order, id",
            Array.Empty<object?>(),
            cancellationToken);
    }

    public async Task<HomeCardsDto> UpdateHomeCardsAsync(Guid adminUserId, HomeCardsDto dto, CancellationToken cancellationToken = default)
    {
        foreach (var card in dto.Cards)
        {
            var title = PlainTextInputValidator.Require(card.Title, "Ana sayfa kart basligi", 200);
            var description = PlainTextInputValidator.Optional(card.Description, "Ana sayfa kart aciklamasi", 1000);
            await ExecuteAsync(
                """
                INSERT INTO home_cards (id, title, description, icon, action_type, sort_order, is_active, updated_at)
                VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, CURRENT_TIMESTAMP)
                ON CONFLICT (id) DO UPDATE SET
                    title = EXCLUDED.title,
                    description = EXCLUDED.description,
                    icon = EXCLUDED.icon,
                    action_type = EXCLUDED.action_type,
                    sort_order = EXCLUDED.sort_order,
                    is_active = EXCLUDED.is_active,
                    updated_at = CURRENT_TIMESTAMP
                """,
                cancellationToken,
                card.Id,
                title,
                description,
                card.Icon,
                card.ActionType,
                card.SortOrder,
                card.IsActive);
        }

        await AddAuditAsync(adminUserId, "update_home_cards", "home_cards", null, dto, cancellationToken);
        return await GetHomeCardsAsync(includeInactive: true, cancellationToken);
    }

    public async Task<AdminPagedResponseDto<AdminAuditLogDto>> GetAuditLogsAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var totalCount = await CountRawAsync("SELECT COUNT(*) FROM admin_audit_logs", cancellationToken);
        var items = await QueryAuditLogsAsync(
            """
            SELECT l.id, l.admin_user_id, u.email, l.action, l.target_type, l.target_id, l.metadata::text, l.created_at
            FROM admin_audit_logs l
            LEFT JOIN users u ON u.id = l.admin_user_id
            ORDER BY l.created_at DESC
            OFFSET @p0 LIMIT @p1
            """,
            new object?[] { (normalizedPage - 1) * normalizedPageSize, normalizedPageSize },
            cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);

        return new AdminPagedResponseDto<AdminAuditLogDto>(items, normalizedPage, normalizedPageSize, totalCount, totalPages);
    }

    private static IQueryable<User> ApplyUserStatusFilter(IQueryable<User> query, string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "active" => query.Where(user => user.IsActive == true && user.LockedUntil == null && user.DeletedAt == null),
            "locked" => query.Where(user => user.LockedUntil != null && user.LockedUntil > DateTime.UtcNow),
            "suspended" => query.Where(user => user.IsActive != true && user.DeletedAt == null),
            "deleted" => query.Where(user => user.DeletedAt != null),
            _ => query
        };
    }

    private async Task<Dictionary<Guid, int>> BuildProductCountsAsync(List<Guid> shopIds, ProductType type, CancellationToken cancellationToken)
    {
        if (shopIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        return await _dbContext.Products
            .AsNoTracking()
            .Where(product => shopIds.Contains(product.ShopId) && product.Type == type)
            .GroupBy(product => product.ShopId)
            .Select(group => new { ShopId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.ShopId, item => item.Count, cancellationToken);
    }

    private static AdminUserListItemDto MapUserListItem(
        User user,
        IReadOnlyDictionary<Guid, int> productCounts,
        IReadOnlyDictionary<Guid, int> courseCounts,
        IReadOnlyDictionary<Guid, int> mediaCounts,
        IReadOnlyDictionary<Guid, int> orderCounts,
        IReadOnlyDictionary<Guid, int> reportCounts,
        IReadOnlyDictionary<Guid, decimal> xp)
    {
        var shopId = user.Shop?.Id;

        return new AdminUserListItemDto(
            Id: user.Id,
            Email: user.Email,
            FullName: user.FullName,
            Role: user.Role.ToString().ToLowerInvariant(),
            Status: GetUserStatus(user),
            ShopId: shopId,
            ShopName: user.Shop?.ShopName,
            AvatarUrl: user.AvatarUrl,
            CreatedAt: user.CreatedAt,
            LastLoginAt: user.LastLoginAt,
            ProductCount: shopId.HasValue ? productCounts.GetValueOrDefault(shopId.Value) : 0,
            CourseCount: shopId.HasValue ? courseCounts.GetValueOrDefault(shopId.Value) : 0,
            MediaCount: shopId.HasValue ? mediaCounts.GetValueOrDefault(shopId.Value) : 0,
            OrderCount: orderCounts.GetValueOrDefault(user.Id),
            ReportCount: reportCounts.GetValueOrDefault(user.Id),
            TotalXp: xp.GetValueOrDefault(user.Id));
    }

    private static string GetUserStatus(User user)
    {
        if (user.DeletedAt is not null)
        {
            return "deleted";
        }

        if (user.LockedUntil is not null && user.LockedUntil > DateTime.UtcNow)
        {
            return "locked";
        }

        return user.IsActive == true ? "active" : "suspended";
    }

    private async Task<User> GetUserForUpdateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);
        return user ?? throw new NotFoundException("Kullanici bulunamadi.");
    }

    private async Task EnsureUserCanBeRestrictedAsync(
        Guid adminUserId,
        User targetUser,
        string selfActionMessage,
        CancellationToken cancellationToken)
    {
        if (targetUser.Id == adminUserId)
        {
            throw new BadRequestException(selfActionMessage);
        }

        if (targetUser.Role != UserRole.Admin)
        {
            return;
        }

        var activeAdminCount = await _dbContext.Users
            .AsNoTracking()
            .CountAsync(user =>
                user.Role == UserRole.Admin &&
                user.IsActive == true &&
                user.DeletedAt == null &&
                (user.LockedUntil == null || user.LockedUntil <= DateTime.UtcNow),
                cancellationToken);

        if (activeAdminCount <= 1)
        {
            throw new BadRequestException("Son aktif admin hesabi bu islemle degistirilemez.");
        }

        throw new BadRequestException("Admin hesaplari bu islemle degistirilemez.");
    }

    private async Task EnsureUserExistsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Users.AnyAsync(user => user.Id == userId, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException("Kullanici bulunamadi.");
        }
    }

    private async Task<List<AdminProductSummaryDto>> GetProductSummariesAsync(Guid shopId, ProductType type, CancellationToken cancellationToken)
    {
        return await _dbContext.Products
            .AsNoTracking()
            .Where(product => product.ShopId == shopId && product.Type == type)
            .OrderByDescending(product => product.CreatedAt)
            .Take(50)
            .Select(product => new AdminProductSummaryDto(
                product.Id,
                product.Title,
                product.Type.ToString(),
                product.Status.ToString(),
                product.IsActive == true,
                product.Price,
                product.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<AdminMediaSummaryDto>> GetMediaSummariesAsync(Guid shopId, CancellationToken cancellationToken)
    {
        return await _dbContext.Media
            .AsNoTracking()
            .Where(media => media.ShopId == shopId)
            .OrderByDescending(media => media.CreatedAt)
            .Take(50)
            .Select(media => new AdminMediaSummaryDto(
                media.Id,
                media.Caption,
                media.Status.ToString(),
                media.IsActive == true,
                media.ViewCount ?? 0,
                media.LikeCount ?? 0,
                media.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    private static AdminCompetitionDto MapCompetition(Contest contest)
    {
        return new AdminCompetitionDto(
            Id: contest.Id,
            Title: contest.Title,
            Description: contest.Description,
            StartDate: contest.StartDate,
            EndDate: contest.EndDate,
            RewardsHidden: contest.RewardsHidden == true,
            PrizePool: contest.PrizePool,
            Status: GetCompetitionStatus(contest),
            IsActive: contest.IsActive == true);
    }

    private static string GetCompetitionStatus(Contest contest)
    {
        if (contest.EndDate <= DateTime.UtcNow && contest.IsActive != true)
        {
            return "finished";
        }

        return contest.IsActive == true ? "active" : "draft";
    }

    private static bool IsActiveCompetitionStatus(string? status)
    {
        return string.Equals(status, "active", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateCompetitionDateRange(AdminUpsertCompetitionDto dto)
    {
        if (dto.StartDate == default || dto.EndDate == default)
        {
            throw new BadRequestException("Yarisma baslangic ve bitis tarihleri zorunludur.");
        }

        if (dto.StartDate >= dto.EndDate)
        {
            throw new BadRequestException("Yarisma baslangic tarihi bitis tarihinden once olmalidir.");
        }
    }

    private async Task<Contest> GetContestForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        var contest = await _dbContext.Contests.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        return contest ?? throw new NotFoundException("Yarisma bulunamadi.");
    }

    private async Task<Contest> GetContestForUpdateWithLockAsync(Guid id, CancellationToken cancellationToken)
    {
        var contest = await _dbContext.Contests
            .FromSqlInterpolated($"SELECT * FROM contests WHERE id = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

        return contest ?? throw new NotFoundException("Yarisma bulunamadi.");
    }

    private static bool IsFinishedCompetition(Contest contest)
    {
        return contest.IsActive != true && contest.EndDate <= DateTime.UtcNow;
    }

    private async Task UpdateReportStatusAsync(Guid adminUserId, Guid reportId, string status, CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            "UPDATE admin_reports SET status = @p1, updated_at = CURRENT_TIMESTAMP WHERE id = @p0",
            cancellationToken,
            reportId,
            status);
        await AddAuditAsync(adminUserId, $"{status}_report", "report", reportId, new { status }, cancellationToken);
    }

    private async Task SendSystemNotificationToAllUsersAsync(string title, string message, Guid referenceId, CancellationToken cancellationToken)
    {
        var userIds = await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.IsActive == true)
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);

        foreach (var userId in userIds)
        {
            await _notificationService.SendNotificationAsync(userId, title, message, NotificationType.System, referenceId);
        }
    }

    private async Task AddAuditAsync(Guid adminUserId, string action, string targetType, Guid? targetId, object metadata, CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            "INSERT INTO admin_audit_logs (admin_user_id, action, target_type, target_id, metadata) VALUES (@p0, @p1, @p2, @p3, CAST(@p4 AS jsonb))",
            cancellationToken,
            adminUserId,
            action,
            targetType,
            targetId,
            JsonSerializer.Serialize(metadata));
    }

    private async Task<Dictionary<Guid, int>> GetReportCountsByUserAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var placeholders = userIds.Select((_, index) => $"@p{index}").ToArray();
        var reports = await QueryKeyCountAsync(
            $"SELECT reported_by_user_id, COUNT(*) FROM admin_reports WHERE reported_by_user_id IN ({string.Join(",", placeholders)}) GROUP BY reported_by_user_id",
            userIds.Cast<object?>().ToArray(),
            cancellationToken);

        return reports;
    }

    private Task<List<AdminReportDto>> GetReportsForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        return QueryReportsAsync(
            """
            SELECT r.id, r.type, r.target_id, r.target_title, r.reported_by_user_id, u.email, r.reason, r.description, r.status, r.created_at
            FROM admin_reports r
            LEFT JOIN users u ON u.id = r.reported_by_user_id
            WHERE r.reported_by_user_id = @p0
            ORDER BY r.created_at DESC
            LIMIT 50
            """,
            new object?[] { userId },
            cancellationToken);
    }

    private async Task<List<AdminWarningDto>> GetWarningsForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await QueryAsync(
            "SELECT id, user_id, admin_user_id, title, message, created_at FROM admin_warnings WHERE user_id = @p0 ORDER BY created_at DESC LIMIT 50",
            new object?[] { userId },
            reader => new AdminWarningDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetDateTime(5)),
            cancellationToken);
    }

    private Task<List<AdminReportDto>> QueryReportsAsync(string sql, object?[] parameters, CancellationToken cancellationToken)
    {
        return QueryAsync(
            sql,
            parameters,
            reader => new AdminReportDto(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8),
                reader.GetDateTime(9)),
            cancellationToken);
    }

    private Task<IReadOnlyList<PulseNewsDto>> QueryPulseNewsAsync(string sql, object?[] parameters, CancellationToken cancellationToken)
    {
        return QueryAsync(
            sql,
            parameters,
            reader => new PulseNewsDto(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetBoolean(5),
                reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                reader.GetDateTime(7),
                reader.IsDBNull(8) ? null : reader.GetDateTime(8)),
            cancellationToken).ContinueWith(task => (IReadOnlyList<PulseNewsDto>)task.Result, cancellationToken);
    }

    private async Task<HomeCardsDto> QueryHomeCardsAsync(string sql, object?[] parameters, CancellationToken cancellationToken)
    {
        var cards = await QueryAsync(
            sql,
            parameters,
            reader => new HomeCardDto(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5),
                reader.GetBoolean(6)),
            cancellationToken);

        return new HomeCardsDto(cards);
    }

    private Task<List<AdminAuditLogDto>> QueryAuditLogsAsync(string sql, object?[] parameters, CancellationToken cancellationToken)
    {
        return QueryAsync(
            sql,
            parameters,
            reader => new AdminAuditLogDto(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.GetString(6),
                reader.GetDateTime(7)),
            cancellationToken);
    }

    private async Task<int> CountRawAsync(string sql, CancellationToken cancellationToken, params object?[] parameters)
    {
        var connection = _dbContext.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        EnlistCurrentTransaction(command);
        command.CommandText = sql;
        AddParameters(command, parameters);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value);
    }

    private async Task<Dictionary<Guid, int>> QueryKeyCountAsync(string sql, object?[] parameters, CancellationToken cancellationToken)
    {
        return (await QueryAsync(
            sql,
            parameters,
            reader => new KeyValuePair<Guid, int>(reader.GetGuid(0), Convert.ToInt32(reader.GetValue(1))),
            cancellationToken)).ToDictionary(item => item.Key, item => item.Value);
    }

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken, params object?[] parameters)
    {
        var connection = _dbContext.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        EnlistCurrentTransaction(command);
        command.CommandText = sql;
        AddParameters(command, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<List<T>> QueryAsync<T>(
        string sql,
        object?[] parameters,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        EnlistCurrentTransaction(command);
        command.CommandText = sql;
        AddParameters(command, parameters);
        var result = new List<T>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(map(reader));
        }

        return result;
    }

    private void EnlistCurrentTransaction(DbCommand command)
    {
        var currentTransaction = _dbContext.Database.CurrentTransaction;
        if (currentTransaction is not null)
        {
            command.Transaction = currentTransaction.GetDbTransaction();
        }
    }

    private static async Task EnsureOpenAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
    }

    private static void AddParameters(DbCommand command, object?[] parameters)
    {
        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = $"p{index}";
            parameter.Value = parameters[index] ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }
}
