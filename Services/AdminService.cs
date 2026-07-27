using System.Data;
using System.Data.Common;
using System.Text.Json;
using CraftoraApi.Data;
using CraftoraApi.DTOs.Admin;
using CraftoraApi.DTOs.Gamification;
using CraftoraApi.DTOs.Notification;
using CraftoraApi.Infrastructure.Security;
using CraftoraApi.Infrastructure.Services;
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
    private const string PublicAssetsBucketName = "public-assets";
    private const int PublicAssetUrlExpiryMinutes = 60;
    private const string ProductReindexLockKey = "search:reindex:products";
    private const string ShopReindexLockKey = "search:reindex:shops";
    private const string MediaReindexLockKey = "search:reindex:media";
    private const string CompetitionCertificateBucketName = "private-products";
    private const int CompetitionCertificateUrlExpiryMinutes = 60 * 24 * 7;
    private const int ExpiringSubscriptionWindowDays = 7;
    private static readonly TimeSpan ProductReindexLockTtl = TimeSpan.FromMinutes(10);
    private static readonly HashSet<string> ReportStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "open", "pending", "reviewing", "resolved", "rejected"
    };
    private static readonly HashSet<string> ReportTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "user", "shop", "product", "media", "course", "comment"
    };

    private readonly AppDbContext _dbContext;
    private readonly INotificationService _notificationService;
    private readonly IGamificationService _gamificationService;
    private readonly ISearchService _searchService;
    private readonly ICacheService _cacheService;
    private readonly IStorageService _storageService;
    private readonly IPdfService _pdfService;

    public AdminService(
        AppDbContext dbContext,
        INotificationService notificationService,
        IGamificationService gamificationService,
        ISearchService searchService,
        ICacheService cacheService,
        IStorageService storageService,
        IPdfService pdfService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _gamificationService = gamificationService ?? throw new ArgumentNullException(nameof(gamificationService));
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _pdfService = pdfService ?? throw new ArgumentNullException(nameof(pdfService));
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

    public async Task<AdminFinanceOverviewDto> GetFinanceOverviewAsync(
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default)
    {
        ValidateFinanceDateRange(startDate, endDate);

        var orderQuery = GetSuccessfulOrderFinanceQuery(startDate, endDate);
        var subscriptionPaymentQuery = GetSuccessfulSubscriptionPaymentQuery(startDate, endDate);
        var now = DateTime.UtcNow;
        var expiringUntil = now.AddDays(ExpiringSubscriptionWindowDays);

        var grossSales = await orderQuery
            .Select(order => (decimal?)order.Amount)
            .SumAsync(cancellationToken) ?? 0m;
        var commissionRevenue = await orderQuery
            .Select(order => order.PlatformFee)
            .SumAsync(cancellationToken) ?? 0m;
        var effectiveCommissionRate = grossSales > 0m
            ? commissionRevenue / grossSales
            : 0m;
        var subscriptionRevenue = await subscriptionPaymentQuery
            .Select(payment => (decimal?)payment.Amount)
            .SumAsync(cancellationToken) ?? 0m;
        var historicalRevenueAvailable = !await _dbContext.SellerSubscriptions
            .AsNoTracking()
            .AnyAsync(subscription => !_dbContext.SellerSubscriptionPayments.Any(payment =>
                payment.SubscriptionId == subscription.Id &&
                payment.Status == "succeeded"), cancellationToken);

        return new AdminFinanceOverviewDto(
            GrossSales: grossSales,
            PlatformCommissionRate: effectiveCommissionRate,
            CommissionRevenue: commissionRevenue,
            SubscriptionRevenue: subscriptionRevenue,
            HistoricalRevenueAvailable: historicalRevenueAvailable,
            TotalPlatformRevenue: commissionRevenue + subscriptionRevenue,
            TotalOrders: await orderQuery.CountAsync(cancellationToken),
            ActiveSubscriptions: await _dbContext.SellerSubscriptions.CountAsync(
                subscription => subscription.Status == SubStatus.Active && subscription.CurrentPeriodEnd >= now,
                cancellationToken),
            ExpiringSubscriptions: await _dbContext.SellerSubscriptions.CountAsync(
                subscription =>
                    subscription.Status == SubStatus.Active &&
                    subscription.CurrentPeriodEnd >= now &&
                    subscription.CurrentPeriodEnd <= expiringUntil,
                cancellationToken));
    }

    public async Task<AdminPagedResponseDto<AdminCommissionListItemDto>> GetCommissionsAsync(
        int page,
        int pageSize,
        DateTime? startDate,
        DateTime? endDate,
        string? query,
        CancellationToken cancellationToken = default)
    {
        ValidateFinanceDateRange(startDate, endDate);

        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        IQueryable<Order> orders = GetSuccessfulOrderFinanceQuery(startDate, endDate)
            .Include(order => order.Payment)
            .Include(order => order.Product)
            .Include(order => order.Shop)
            .ThenInclude(shop => shop.User);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalizedQuery = query.Trim().ToLowerInvariant();
            orders = orders.Where(order =>
                order.OrderNumber.ToLower().Contains(normalizedQuery) ||
                order.Shop.ShopName.ToLower().Contains(normalizedQuery) ||
                order.Product.Title.ToLower().Contains(normalizedQuery) ||
                order.Shop.User.Email.ToLower().Contains(normalizedQuery));
        }

        var totalCount = await orders.CountAsync(cancellationToken);
        var records = await orders
            .OrderByDescending(order => order.Payment!.CreatedAt ?? order.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        var items = records.Select(order =>
        {
            var platformFee = order.PlatformFee ?? 0m;
            var commissionRate = order.CommissionRate ??
                (order.Amount > 0m ? platformFee / order.Amount : 0m);
            return new AdminCommissionListItemDto(
                OrderId: order.Id,
                OrderNumber: order.OrderNumber,
                SellerId: order.Shop.UserId,
                ShopId: order.ShopId,
                ShopName: order.Shop.ShopName,
                ProductId: order.ProductId,
                ProductTitle: order.Product.Title,
                GrossAmount: order.Amount,
                CommissionRate: commissionRate,
                PlatformFee: platformFee,
                SellerEarnings: order.SellerEarnings ?? order.Amount - platformFee,
                Currency: order.Currency ?? "TRY",
                PaymentStatus: order.Payment!.Status.ToString().ToLowerInvariant(),
                CreatedAt: order.Payment.CreatedAt ?? order.CreatedAt);
        }).ToList();

        return new AdminPagedResponseDto<AdminCommissionListItemDto>(
            items,
            normalizedPage,
            normalizedPageSize,
            totalCount,
            CalculateTotalPages(totalCount, normalizedPageSize));
    }

    public async Task<AdminPagedResponseDto<AdminSubscriptionFinanceListItemDto>> GetSubscriptionFinanceAsync(
        int page,
        int pageSize,
        string? status,
        string? query,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var now = DateTime.UtcNow;
        var subscriptions = _dbContext.SellerSubscriptions
            .AsNoTracking()
            .Include(subscription => subscription.Plan)
            .Include(subscription => subscription.Shop)
            .ThenInclude(shop => shop.User)
            .AsQueryable();

        subscriptions = ApplySubscriptionStatusFilter(subscriptions, status, now);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalizedQuery = query.Trim().ToLowerInvariant();
            subscriptions = subscriptions.Where(subscription =>
                subscription.Shop.ShopName.ToLower().Contains(normalizedQuery) ||
                subscription.Shop.User.Email.ToLower().Contains(normalizedQuery) ||
                (subscription.Shop.User.FullName != null && subscription.Shop.User.FullName.ToLower().Contains(normalizedQuery)));
        }

        var totalCount = await subscriptions.CountAsync(cancellationToken);
        var records = await subscriptions
            .OrderByDescending(subscription => subscription.UpdatedAt ?? subscription.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        var subscriptionIds = records.Select(subscription => subscription.Id).ToList();
        var latestPayments = subscriptionIds.Count == 0
            ? new Dictionary<Guid, SellerSubscriptionPayment>()
            : (await _dbContext.SellerSubscriptionPayments
                .AsNoTracking()
                .Where(payment =>
                    subscriptionIds.Contains(payment.SubscriptionId) &&
                    payment.Status == "succeeded")
                .OrderByDescending(payment => payment.CreatedAt)
                .ToListAsync(cancellationToken))
                .GroupBy(payment => payment.SubscriptionId)
                .ToDictionary(group => group.Key, group => group.First());

        var items = records.Select(subscription =>
        {
            latestPayments.TryGetValue(subscription.Id, out var lastPayment);
            var financeStatus = GetFinanceSubscriptionStatus(subscription, now);
            return new AdminSubscriptionFinanceListItemDto(
                SubscriptionId: subscription.Id,
                UserId: subscription.Shop.UserId,
                ShopId: subscription.ShopId,
                ShopName: subscription.Shop.ShopName,
                OwnerName: subscription.Shop.User.FullName,
                OwnerEmail: subscription.Shop.User.Email,
                PlanName: subscription.Plan.Name,
                Amount: lastPayment?.Amount ?? 0m,
                Currency: lastPayment?.Currency ?? subscription.Currency ?? "TRY",
                Status: financeStatus,
                ShopStatus: GetFinanceShopStatus(subscription.Shop),
                StartedAt: subscription.CreatedAt,
                ExpiresAt: subscription.CurrentPeriodEnd,
                RemainingDays: CalculateRemainingDays(subscription.CurrentPeriodEnd, now, financeStatus),
                LastPaymentAt: lastPayment?.CreatedAt);
        }).ToList();

        return new AdminPagedResponseDto<AdminSubscriptionFinanceListItemDto>(
            items,
            normalizedPage,
            normalizedPageSize,
            totalCount,
            CalculateTotalPages(totalCount, normalizedPageSize));
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

    public async Task<int> ReindexShopsAsync(
        Guid adminUserId,
        CancellationToken cancellationToken = default)
    {
        var lockValue = Guid.NewGuid().ToString("N");
        bool lockAcquired;
        try
        {
            lockAcquired = await _cacheService.TryAcquireLockAsync(
                ShopReindexLockKey,
                lockValue,
                ProductReindexLockTtl);
        }
        catch
        {
            throw new ExternalServiceException("Redis", "Reindex kilidi alinamadi. Lutfen tekrar deneyin.");
        }

        if (!lockAcquired)
        {
            throw new ConflictException("Magaza reindex islemi zaten devam ediyor.");
        }

        try
        {
            var indexedCount = await _searchService.ReindexShopsAsync(cancellationToken);
            await AddAuditAsync(
                adminUserId,
                "reindex_shops",
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
                await _cacheService.ReleaseLockAsync(ShopReindexLockKey, lockValue);
            }
            catch
            {
                // The lock expires automatically; a release failure must not hide a successful reindex.
            }
        }
    }

    public async Task<int> ReindexMediaAsync(
        Guid adminUserId,
        CancellationToken cancellationToken = default)
    {
        var lockValue = Guid.NewGuid().ToString("N");
        bool lockAcquired;
        try
        {
            lockAcquired = await _cacheService.TryAcquireLockAsync(
                MediaReindexLockKey,
                lockValue,
                ProductReindexLockTtl);
        }
        catch
        {
            throw new ExternalServiceException("Redis", "Reindex kilidi alinamadi. Lutfen tekrar deneyin.");
        }

        if (!lockAcquired)
        {
            throw new ConflictException("Medya reindex islemi zaten devam ediyor.");
        }

        try
        {
            var indexedCount = await _searchService.ReindexMediaAsync(cancellationToken);
            await AddAuditAsync(
                adminUserId,
                "reindex_media",
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
                await _cacheService.ReleaseLockAsync(MediaReindexLockKey, lockValue);
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
        ArgumentNullException.ThrowIfNull(dto);
        if (dto.Until <= DateTime.UtcNow)
        {
            throw new BadRequestException("Kilit bitis tarihi gecmis bir tarih olamaz.");
        }

        var user = await GetUserForUpdateAsync(userId, cancellationToken);
        await EnsureUserCanBeRestrictedAsync(
            adminUserId,
            user,
            "Kendi hesabinizi kilitleyemezsiniz.",
            cancellationToken);

        user.LockedUntil = dto.Until;
        user.LockReason = dto.Reason;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync(adminUserId, "lock_user", "user", userId, new { dto.Reason, dto.Until }, cancellationToken);
    }

    public async Task UnlockUserAsync(Guid adminUserId, Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await GetUserForUpdateAsync(userId, cancellationToken);
        user.LockedUntil = null;
        user.LockReason = null;
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
        user.LockReason = null;
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
        if (page < 1 || pageSize is < 1 or > 100)
        {
            throw new BadRequestException("Rapor sayfalama degerleri gecersiz.");
        }

        if (!string.IsNullOrWhiteSpace(status) && !ReportStatuses.Contains(status.Trim()))
        {
            throw new BadRequestException("Gecersiz rapor durumu.");
        }

        if (!string.IsNullOrWhiteSpace(type) && !ReportTypes.Contains(type.Trim()))
        {
            throw new BadRequestException("Gecersiz rapor hedef tipi.");
        }

        var normalizedPage = page;
        var normalizedPageSize = pageSize;
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

    public async Task<AdminReportTargetDto> GetReportTargetAsync(
        Guid adminUserId,
        Guid reportId,
        CancellationToken cancellationToken = default)
    {
        var report = await GetReportRecordAsync(reportId, cancellationToken);
        await using (var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            await MarkReportReviewingAsync(adminUserId, report, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await BuildReportTargetAsync(report, cancellationToken);
    }

    public async Task WarnReportTargetAsync(
        Guid adminUserId,
        Guid reportId,
        AdminWarnUserRequestDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var title = PlainTextInputValidator.Require(dto.Title, "Uyari basligi", 200);
        var message = PlainTextInputValidator.Require(dto.Message, "Uyari mesaji", 1000);
        var report = await GetReportRecordAsync(reportId, cancellationToken);
        var owner = await ResolveReportOwnerAsync(report.Type, report.TargetId, cancellationToken);

        if (owner.UserId is null)
        {
            throw new NotFoundException("Rapor hedefinin sahibi bulunamadi.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await MarkReportReviewingAsync(adminUserId, report, cancellationToken);
        await ExecuteAsync(
            "INSERT INTO admin_warnings (user_id, admin_user_id, title, message) VALUES (@p0, @p1, @p2, @p3)",
            cancellationToken,
            owner.UserId,
            adminUserId,
            title,
            message);
        await AddAuditAsync(
            adminUserId,
            "warn_report_target",
            "report",
            reportId,
            new { report.Type, report.TargetId, owner.UserId, Title = title, Message = message },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await _notificationService.SendNotificationAsync(owner.UserId.Value, title, message, NotificationType.System, reportId);
    }

    public async Task BlockReportTargetAsync(
        Guid adminUserId,
        Guid reportId,
        AdminBlockReportTargetRequestDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var reason = PlainTextInputValidator.Require(dto.Reason, "Engelleme nedeni", 1000);
        var report = await GetReportRecordAsync(reportId, cancellationToken);
        var normalizedType = report.Type.Trim().ToLowerInvariant();
        Guid? searchIndexTargetId = null;
        string? searchIndexType = null;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await MarkReportReviewingAsync(adminUserId, report, cancellationToken);

        switch (normalizedType)
        {
            case "product":
                await ArchiveReportedProductAsync(report.TargetId, cancellationToken);
                searchIndexTargetId = report.TargetId;
                searchIndexType = "product";
                break;
            case "course":
                searchIndexTargetId = await ArchiveReportedCourseAsync(report.TargetId, cancellationToken);
                searchIndexType = "product";
                break;
            case "media":
                await DeactivateReportedMediaAsync(report.TargetId, cancellationToken);
                searchIndexTargetId = report.TargetId;
                searchIndexType = "media";
                break;
            case "shop":
                await SuspendReportedShopAsync(adminUserId, report.TargetId, cancellationToken);
                searchIndexTargetId = report.TargetId;
                searchIndexType = "shop";
                break;
            case "user":
                await SuspendReportedUserAsync(adminUserId, report.TargetId, cancellationToken);
                break;
            case "comment":
                await DeleteReportedCommentAsync(report.TargetId, cancellationToken);
                break;
            default:
                throw new BadRequestException("Rapor hedef tipi engellenemiyor.");
        }

        await AddAuditAsync(
            adminUserId,
            "block_report_target",
            "report",
            reportId,
            new { report.Type, report.TargetId, Reason = reason },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (searchIndexTargetId.HasValue)
        {
            try
            {
                if (searchIndexType == "product")
                {
                    await _searchService.DeleteProductIndexAsync(searchIndexTargetId.Value, cancellationToken);
                }
                else if (searchIndexType == "media")
                {
                    await _searchService.DeleteMediaIndexAsync(searchIndexTargetId.Value, cancellationToken);
                }
                else if (searchIndexType == "shop")
                {
                    await _searchService.DeleteShopIndexAsync(searchIndexTargetId.Value, cancellationToken);
                }
            }
            catch
            {
                // Public search has active-status filters; the next reindex also removes stale documents.
            }
        }
    }

    public async Task<IReadOnlyList<AdminCompetitionDto>> GetCompetitionsAsync(CancellationToken cancellationToken = default)
    {
        var contests = await _dbContext.Contests
            .AsNoTracking()
            .OrderByDescending(contest => contest.StartDate)
            .ToListAsync(cancellationToken);

        var contestIds = contests.Select(contest => contest.Id).ToList();
        var rewardedCounts = contestIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await GetCompetitionRewardCountsAsync(contestIds, cancellationToken);

        return contests
            .Select(contest => MapCompetition(contest, rewardedCounts.GetValueOrDefault(contest.Id)))
            .ToList();
    }

    public async Task<AdminCompetitionLeaderboardResponseDto> GetCompetitionLeaderboardAsync(
        Guid id,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var contest = await _dbContext.Contests
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("Yarisma bulunamadi.");
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var rows = await BuildCompetitionScoreRowsAsync(contest, cancellationToken);
        var totalCount = rows.Count;
        var pageRows = rows
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToList();
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);

        return new AdminCompetitionLeaderboardResponseDto(
            contest.Id,
            GetCompetitionStatus(contest),
            await MapAdminCompetitionLeaderboardItemsAsync(pageRows, (normalizedPage - 1) * normalizedPageSize, cancellationToken),
            normalizedPage,
            normalizedPageSize,
            totalCount,
            totalPages);
    }

    public async Task<AdminCompetitionParticipantsResponseDto> GetCompetitionParticipantsAsync(
        Guid id,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var contest = await _dbContext.Contests
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException("Yarisma bulunamadi.");
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var scoreRows = await BuildCompetitionScoreRowsAsync(contest, cancellationToken);
        var scoresByUserId = scoreRows.ToDictionary(row => row.UserId, row => row.Score);
        var ranksByUserId = scoreRows
            .Select((row, index) => new
            {
                row.UserId,
                Rank = row.Score == 0 ? (int?)null : index + 1
            })
            .ToDictionary(item => item.UserId, item => item.Rank);

        var participants = await _dbContext.ContestResults
            .AsNoTracking()
            .Where(result => result.ContestId == contest.Id)
            .Include(result => result.User)
                .ThenInclude(user => user.Shop)
            .ToListAsync(cancellationToken);
        var totalCount = participants.Count;
        var items = participants
            .Select(result =>
            {
                var user = result.User;
                var shop = user.Shop;
                var score = scoresByUserId.GetValueOrDefault(result.UserId);
                return new AdminCompetitionParticipantDto(
                    result.UserId,
                    shop?.ShopName ?? user.FullName ?? user.Email,
                    GeneratePublicAssetUrl(user.AvatarUrl),
                    shop?.Id,
                    shop?.ShopName,
                    GeneratePublicAssetUrl(shop?.LogoUrl),
                    result.JoinedAt,
                    score,
                    ranksByUserId.GetValueOrDefault(result.UserId));
            })
            .OrderBy(item => item.Rank is null ? 1 : 0)
            .ThenBy(item => item.Rank)
            .ThenByDescending(item => item.JoinedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToList();
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);

        return new AdminCompetitionParticipantsResponseDto(
            contest.Id,
            items,
            normalizedPage,
            normalizedPageSize,
            totalCount,
            totalPages);
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

        return MapCompetition(contest, 0);
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

        var rewardedCount = await CountRawAsync(
            "SELECT COUNT(*) FROM admin_competition_rewards WHERE contest_id = @p0",
            cancellationToken,
            contest.Id);
        return MapCompetition(contest, rewardedCount);
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
        ArgumentNullException.ThrowIfNull(dto);
        if (dto.Winners is null || dto.Winners.Count == 0)
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

        var leaderboard = await BuildCompetitionScoreRowsAsync(contest, cancellationToken);
        var ranksByUserId = leaderboard
            .Select((row, index) => new { row.UserId, Rank = index + 1 })
            .ToDictionary(item => item.UserId, item => item.Rank);
        foreach (var winner in dto.Winners)
        {
            ValidateRewardWinner(winner);
            if (!ranksByUserId.TryGetValue(winner.UserId, out var calculatedRank) || calculatedRank != winner.Rank)
            {
                throw new BadRequestException("Kazananlar yarismaya ait leaderboard siralamasiyla eslesmelidir.");
            }
        }

        contest.IsActive = false;
        if (contest.EndDate > DateTime.UtcNow)
        {
            contest.EndDate = DateTime.UtcNow;
        }

        var pendingNotifications = new List<CompetitionRewardNotification>();
        foreach (var winner in dto.Winners)
        {
            var rewardType = winner.RewardType.Trim().ToLowerInvariant();
            string? certificateObjectKey = null;

            if (rewardType == "premium_1_month")
            {
                await GrantPremiumOneMonthAsync(winner.UserId, cancellationToken);
            }
            else if (rewardType == "certificate")
            {
                var user = await _dbContext.Users
                    .SingleOrDefaultAsync(item => item.Id == winner.UserId, cancellationToken)
                    ?? throw new NotFoundException("Kullanici bulunamadi.");
                var certificateBytes = await _pdfService.GenerateCompetitionCertificatePdfAsync(
                    new CompetitionCertificateData(
                        contest.Title,
                        user.FullName ?? user.Email,
                        winner.Rank,
                        DateTime.UtcNow),
                    cancellationToken);
                certificateObjectKey = $"competition-certificates/{contest.Id:D}/{winner.UserId:D}/{Guid.NewGuid():N}.pdf";
                await _storageService.UploadFileAsync(
                    CompetitionCertificateBucketName,
                    certificateObjectKey,
                    certificateBytes,
                    "application/pdf",
                    cancellationToken);
            }

            await ExecuteAsync(
                """
                INSERT INTO admin_competition_rewards (contest_id, user_id, rank, reward_type, amount, currency, note, certificate_url)
                VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7)
                """,
                cancellationToken,
                id,
                winner.UserId,
                winner.Rank,
                rewardType,
                rewardType == "money" ? winner.Amount : null,
                rewardType == "money" ? NormalizeCurrency(winner.Currency) : null,
                winner.Note,
                certificateObjectKey);

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

            await AddAuditAsync(
                adminUserId,
                "distribute_competition_reward",
                "competition_reward",
                id,
                new
                {
                    winner.UserId,
                    winner.Rank,
                    RewardType = rewardType,
                    winner.Amount,
                    Currency = rewardType == "money" ? NormalizeCurrency(winner.Currency) : null,
                    CertificateUrl = certificateObjectKey
                },
                cancellationToken);
            pendingNotifications.Add(CreateCompetitionRewardNotification(contest, winner, rewardType));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync(adminUserId, "distribute_rewards", "competition", id, dto, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var notification in pendingNotifications)
        {
            await _notificationService.SendNotificationAsync(
                notification.UserId,
                notification.Title,
                notification.Body,
                NotificationType.System,
                id,
                "competition");
        }
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

    private AdminUserListItemDto MapUserListItem(
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
            ShopLogoPublicUrl: GeneratePublicAssetUrl(user.Shop?.LogoUrl),
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

    private string? GeneratePublicAssetUrl(string? objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return null;
        }

        return _storageService.GeneratePresignedDownloadUrl(
            PublicAssetsBucketName,
            objectKey,
            PublicAssetUrlExpiryMinutes);
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

    private async Task<Dictionary<Guid, int>> GetCompetitionRewardCountsAsync(
        IReadOnlyCollection<Guid> contestIds,
        CancellationToken cancellationToken)
    {
        var placeholders = contestIds.Select((_, index) => $"@p{index}").ToArray();
        var rows = await QueryAsync(
            $"SELECT contest_id, COUNT(*) FROM admin_competition_rewards WHERE contest_id IN ({string.Join(",", placeholders)}) GROUP BY contest_id",
            contestIds.Cast<object?>().ToArray(),
            reader => new KeyValuePair<Guid, int>(reader.GetGuid(0), Convert.ToInt32(reader.GetValue(1))),
            cancellationToken);
        return rows.ToDictionary(item => item.Key, item => item.Value);
    }

    private async Task<List<CompetitionScoreRow>> BuildCompetitionScoreRowsAsync(
        Contest contest,
        CancellationToken cancellationToken)
    {
        var scoreRows = await _dbContext.PointLogs
            .AsNoTracking()
            .Where(log =>
                log.CreatedAt >= contest.StartDate &&
                log.CreatedAt <= contest.EndDate &&
                _dbContext.ContestResults.Any(result =>
                    result.ContestId == contest.Id &&
                    result.UserId == log.UserId &&
                    log.CreatedAt >= (result.JoinedAt ?? contest.StartDate)))
            .GroupBy(log => log.UserId)
            .Select(group => new
            {
                UserId = group.Key,
                Score = group.Sum(log => log.PointsEarned),
                SalesPoints = group.Sum(log => log.ActionType == "make_sale" ? log.PointsEarned : 0m),
                ViewPoints = group.Sum(log => log.ActionType == "watch_reels" ? log.PointsEarned : 0m),
                EngagementPoints = group.Sum(log => log.ActionType == "receive_like" ? log.PointsEarned : 0m),
                LearningPoints = group.Sum(log => log.ActionType == "complete_lesson" ? log.PointsEarned : 0m)
            })
            .OrderByDescending(row => row.Score)
            .ThenBy(row => row.UserId)
            .ToListAsync(cancellationToken);

        return scoreRows
            .Select(row => new CompetitionScoreRow(
                row.UserId,
                row.Score,
                row.SalesPoints,
                row.ViewPoints,
                row.EngagementPoints,
                row.LearningPoints))
            .ToList();
    }

    private async Task<IReadOnlyList<AdminCompetitionLeaderboardItemDto>> MapAdminCompetitionLeaderboardItemsAsync(
        IReadOnlyList<CompetitionScoreRow> rows,
        int rankOffset,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return Array.Empty<AdminCompetitionLeaderboardItemDto>();
        }

        var userIds = rows.Select(row => row.UserId).ToList();
        var users = await _dbContext.Users
            .AsNoTracking()
            .Include(user => user.Shop)
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, cancellationToken);

        return rows.Select((row, index) =>
        {
            users.TryGetValue(row.UserId, out var user);
            var shop = user?.Shop?.IsActive == true ? user.Shop : null;
            return new AdminCompetitionLeaderboardItemDto(
                rankOffset + index + 1,
                row.UserId,
                shop?.ShopName ?? user?.FullName ?? user?.Email,
                GeneratePublicAssetUrl(user?.AvatarUrl),
                shop?.Id,
                shop?.ShopName,
                GeneratePublicAssetUrl(shop?.LogoUrl),
                row.Score,
                row.SalesPoints,
                row.ViewPoints,
                row.EngagementPoints,
                row.LearningPoints);
        }).ToList();
    }

    private static void ValidateRewardWinner(AdminRewardWinnerDto winner)
    {
        var rewardType = winner.RewardType?.Trim().ToLowerInvariant();
        if (rewardType is not ("money" or "premium_1_month" or "certificate"))
        {
            throw new BadRequestException("Odul tipi money, premium_1_month veya certificate olmalidir.");
        }

        if (winner.Rank < 1)
        {
            throw new BadRequestException("Derece 1 veya daha buyuk olmalidir.");
        }

        if (rewardType == "money" && (!winner.Amount.HasValue || winner.Amount.Value <= 0))
        {
            throw new BadRequestException("Para odulu icin 0'dan buyuk tutar zorunludur.");
        }

    }

    private static string NormalizeCurrency(string? currency)
    {
        var normalized = string.IsNullOrWhiteSpace(currency) ? "TRY" : currency.Trim().ToUpperInvariant();
        if (normalized.Length != 3)
        {
            throw new BadRequestException("Para birimi uc harfli ISO kodu olmalidir.");
        }

        return normalized;
    }

    private async Task GrantPremiumOneMonthAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .Include(item => item.Shop)
            .ThenInclude(shop => shop!.SellerSubscription)
            .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken)
            ?? throw new NotFoundException("Kullanici bulunamadi.");
        var shop = user.Shop ?? throw new BadRequestException("Premium odulu mevcut abonelik modeli nedeniyle magazasi olan kullanicilara verilebilir.");
        var subscription = shop.SellerSubscription;
        var now = DateTime.UtcNow;
        var professionalPlan = await _dbContext.SellerSubscriptionPlans
            .SingleOrDefaultAsync(plan => plan.Code == "professional" && plan.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("Professional abonelik plani bulunamadi.");

        if (subscription is null)
        {
            subscription = new SellerSubscription
            {
                ShopId = shop.Id,
                PlanId = professionalPlan.Id,
                ProviderSubscriptionId = $"competition_reward_{Guid.NewGuid():N}",
                Status = SubStatus.Active,
                CurrentPeriodEnd = now.AddMonths(1),
                Amount = 0,
                Currency = "TRY",
                PaymentProvider = "competition_reward",
                CreatedAt = now,
                UpdatedAt = now
            };
            _dbContext.SellerSubscriptions.Add(subscription);
        }
        else
        {
            subscription.PlanId = professionalPlan.Id;
            subscription.Status = SubStatus.Active;
            subscription.CurrentPeriodEnd = (subscription.CurrentPeriodEnd > now ? subscription.CurrentPeriodEnd : now).AddMonths(1);
            subscription.GracePeriodEnd = null;
            subscription.UpdatedAt = now;
        }

        shop.IsActive = true;
        shop.UpdatedAt = now;
        if (user.Role == UserRole.User)
        {
            user.Role = UserRole.Seller;
        }
    }

    private static CompetitionRewardNotification CreateCompetitionRewardNotification(
        Contest contest,
        AdminRewardWinnerDto winner,
        string rewardType)
    {
        return rewardType switch
        {
            "money" => new CompetitionRewardNotification(
                winner.UserId,
                "Yarisma odulun hazir",
                $"{contest.Title} yarismasinda {winner.Rank}. oldunuz. {winner.Amount:0.00} {NormalizeCurrency(winner.Currency)} para odulu kaydedildi."),
            "premium_1_month" => new CompetitionRewardNotification(
                winner.UserId,
                "1 aylik premium odulun tanimlandi",
                $"{contest.Title} yarismasindaki {winner.Rank}. dereceniz icin aboneliginiz 1 ay uzatildi."),
            _ => new CompetitionRewardNotification(
                winner.UserId,
                "Yarisma belgen hazir",
                $"{contest.Title} yarismasindaki {winner.Rank}. dereceniz icin belgeniz hazirlandi.")
        };
    }

    private static AdminCompetitionDto MapCompetition(Contest contest, int rewardedCount)
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
            IsActive: contest.IsActive == true,
            RewardsDistributed: rewardedCount > 0,
            RewardedCount: rewardedCount);
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
        var report = await GetReportRecordAsync(reportId, cancellationToken);
        if (IsFinalReportStatus(report.Status))
        {
            throw new ConflictException("Bu rapor zaten sonuclandirildi.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(
            "UPDATE admin_reports SET status = @p1, updated_at = CURRENT_TIMESTAMP WHERE id = @p0",
            cancellationToken,
            reportId,
            status);
        await AddAuditAsync(adminUserId, $"{status}_report", "report", reportId, new { status }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<AdminReportRecord> GetReportRecordAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var reports = await QueryAsync(
            "SELECT id, type, target_id, target_title, reported_by_user_id, reason, description, status, created_at FROM admin_reports WHERE id = @p0",
            new object?[] { reportId },
            reader => new AdminReportRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7),
                reader.GetDateTime(8),
                null),
            cancellationToken);

        return reports.SingleOrDefault() ?? throw new NotFoundException("Sikayet bulunamadi.");
    }

    private async Task MarkReportReviewingAsync(Guid adminUserId, AdminReportRecord report, CancellationToken cancellationToken)
    {
        if (!string.Equals(report.Status, "open", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(report.Status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await ExecuteAsync(
            "UPDATE admin_reports SET status = 'reviewing', updated_at = CURRENT_TIMESTAMP WHERE id = @p0",
            cancellationToken,
            report.Id);
        await AddAuditAsync(adminUserId, "review_report", "report", report.Id, new { status = "reviewing" }, cancellationToken);
    }

    private async Task<AdminReportTargetDto> BuildReportTargetAsync(AdminReportRecord report, CancellationToken cancellationToken)
    {
        var owner = await ResolveReportOwnerAsync(report.Type, report.TargetId, cancellationToken);
        var target = await ResolveReportTargetDetailsAsync(report.Type, report.TargetId, cancellationToken);

        return new AdminReportTargetDto(
            report.Id,
            report.Type,
            report.TargetId,
            owner.UserId,
            owner.Name,
            owner.Email,
            owner.ShopId,
            target);
    }

    private async Task<ReportTargetOwner> ResolveReportOwnerAsync(string targetType, Guid targetId, CancellationToken cancellationToken)
    {
        switch (targetType.Trim().ToLowerInvariant())
        {
            case "product":
            {
                var product = await _dbContext.Products.AsNoTracking()
                    .Include(item => item.Shop).ThenInclude(shop => shop.User)
                    .SingleOrDefaultAsync(item => item.Id == targetId, cancellationToken);
                return product is null ? ReportTargetOwner.Empty : CreateOwner(product.Shop.User, product.Shop);
            }
            case "course":
            {
                var course = await _dbContext.Courses.AsNoTracking()
                    .Include(item => item.Product).ThenInclude(product => product.Shop).ThenInclude(shop => shop.User)
                    .SingleOrDefaultAsync(item => item.Id == targetId, cancellationToken);
                if (course is not null)
                {
                    return CreateOwner(course.Product.Shop.User, course.Product.Shop);
                }

                var product = await _dbContext.Products.AsNoTracking()
                    .Include(item => item.Shop).ThenInclude(shop => shop.User)
                    .SingleOrDefaultAsync(item => item.Id == targetId && item.Type == ProductType.Course, cancellationToken);
                return product is null ? ReportTargetOwner.Empty : CreateOwner(product.Shop.User, product.Shop);
            }
            case "media":
            {
                var media = await _dbContext.Media.AsNoTracking()
                    .Include(item => item.Shop).ThenInclude(shop => shop.User)
                    .SingleOrDefaultAsync(item => item.Id == targetId, cancellationToken);
                return media is null ? ReportTargetOwner.Empty : CreateOwner(media.Shop.User, media.Shop);
            }
            case "shop":
            {
                var shop = await _dbContext.Shops.AsNoTracking().Include(item => item.User)
                    .SingleOrDefaultAsync(item => item.Id == targetId, cancellationToken);
                return shop is null ? ReportTargetOwner.Empty : CreateOwner(shop.User, shop);
            }
            case "user":
            {
                var user = await _dbContext.Users.AsNoTracking().Include(item => item.Shop)
                    .SingleOrDefaultAsync(item => item.Id == targetId, cancellationToken);
                return user is null ? ReportTargetOwner.Empty : CreateOwner(user, user.Shop);
            }
            case "comment":
            {
                var comment = await _dbContext.MediaComments.AsNoTracking()
                    .Include(item => item.User).ThenInclude(user => user.Shop)
                    .SingleOrDefaultAsync(item => item.Id == targetId, cancellationToken);
                return comment is null ? ReportTargetOwner.Empty : CreateOwner(comment.User, comment.User.Shop);
            }
            default:
                return ReportTargetOwner.Empty;
        }
    }

    private async Task<object> ResolveReportTargetDetailsAsync(string targetType, Guid targetId, CancellationToken cancellationToken)
    {
        switch (targetType.Trim().ToLowerInvariant())
        {
            case "product":
                return await GetProductTargetDetailsAsync(targetId, cancellationToken);
            case "course":
                return await GetCourseTargetDetailsAsync(targetId, cancellationToken);
            case "media":
                return await GetMediaTargetDetailsAsync(targetId, cancellationToken);
            case "shop":
                return await GetShopTargetDetailsAsync(targetId, cancellationToken);
            case "user":
                return await GetUserTargetDetailsAsync(targetId, cancellationToken);
            case "comment":
                return await GetCommentTargetDetailsAsync(targetId, cancellationToken);
            default:
                throw new BadRequestException("Bilinmeyen rapor hedef tipi.");
        }
    }

    private async Task<object> GetProductTargetDetailsAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await _dbContext.Products.AsNoTracking()
            .Include(item => item.Shop).ThenInclude(shop => shop.User)
            .SingleOrDefaultAsync(item => item.Id == productId, cancellationToken)
            ?? throw new NotFoundException("Urun bulunamadi.");

        return new
        {
            product.Id,
            product.Title,
            product.Description,
            product.Price,
            product.OriginalPrice,
            product.Currency,
            Type = product.Type.ToString().ToLowerInvariant(),
            Status = product.Status.ToString().ToLowerInvariant(),
            product.IsActive,
            CoverImageUrl = product.CoverImageUrl,
            CoverImagePublicUrl = GeneratePublicAssetUrl(product.CoverImageUrl),
            product.PreviewVideoUrl,
            product.CreatedAt,
            product.UpdatedAt,
            Shop = CreateShopTarget(product.Shop),
            Owner = CreateOwnerTarget(product.Shop.User)
        };
    }

    private async Task<object> GetCourseTargetDetailsAsync(Guid courseId, CancellationToken cancellationToken)
    {
        var course = await _dbContext.Courses.AsNoTracking()
            .Include(item => item.Product).ThenInclude(product => product.Shop).ThenInclude(shop => shop.User)
            .SingleOrDefaultAsync(item => item.Id == courseId, cancellationToken);

        if (course is null)
        {
            return await GetProductTargetDetailsAsync(courseId, cancellationToken);
        }

        var product = course.Product;
        return new
        {
            CourseId = course.Id,
            ProductId = product.Id,
            product.Title,
            product.Description,
            product.Price,
            product.OriginalPrice,
            product.Currency,
            product.Status,
            product.IsActive,
            course.Level,
            course.TotalDurationInMinutes,
            course.IsCertificateIncluded,
            CoverImageUrl = product.CoverImageUrl,
            CoverImagePublicUrl = GeneratePublicAssetUrl(product.CoverImageUrl),
            Shop = CreateShopTarget(product.Shop),
            Owner = CreateOwnerTarget(product.Shop.User)
        };
    }

    private async Task<object> GetMediaTargetDetailsAsync(Guid mediaId, CancellationToken cancellationToken)
    {
        var media = await _dbContext.Media.AsNoTracking()
            .Include(item => item.Shop).ThenInclude(shop => shop.User)
            .Include(item => item.Product)
            .SingleOrDefaultAsync(item => item.Id == mediaId, cancellationToken)
            ?? throw new NotFoundException("Medya bulunamadi.");

        return new
        {
            media.Id,
            media.Caption,
            Status = media.Status.ToString().ToLowerInvariant(),
            media.IsActive,
            ThumbnailUrl = media.ThumbnailUrl,
            ThumbnailPublicUrl = GeneratePublicAssetUrl(media.ThumbnailUrl),
            media.VideoUrl,
            media.ViewCount,
            media.LikeCount,
            media.SaveCount,
            media.ShareCount,
            media.CommentCount,
            media.Hashtags,
            media.CreatedAt,
            Shop = CreateShopTarget(media.Shop),
            Owner = CreateOwnerTarget(media.Shop.User),
            Product = media.Product is null ? null : new { media.Product.Id, media.Product.Title }
        };
    }

    private async Task<object> GetShopTargetDetailsAsync(Guid shopId, CancellationToken cancellationToken)
    {
        var shop = await _dbContext.Shops.AsNoTracking().Include(item => item.User)
            .SingleOrDefaultAsync(item => item.Id == shopId, cancellationToken)
            ?? throw new NotFoundException("Magaza bulunamadi.");

        return new
        {
            shop.Id,
            shop.ShopName,
            shop.Slug,
            shop.ShortDescription,
            shop.Description,
            shop.LogoUrl,
            LogoPublicUrl = GeneratePublicAssetUrl(shop.LogoUrl),
            shop.BannerUrl,
            BannerPublicUrl = GeneratePublicAssetUrl(shop.BannerUrl),
            shop.FollowerCount,
            shop.Rating,
            shop.IsVerified,
            shop.IsActive,
            shop.CreatedAt,
            Owner = CreateOwnerTarget(shop.User)
        };
    }

    private async Task<object> GetUserTargetDetailsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.AsNoTracking().Include(item => item.Shop)
            .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken)
            ?? throw new NotFoundException("Kullanici bulunamadi.");

        return new
        {
            user.Id,
            user.FullName,
            user.Email,
            Role = user.Role.ToString().ToLowerInvariant(),
            user.AvatarUrl,
            AvatarPublicUrl = GeneratePublicAssetUrl(user.AvatarUrl),
            user.IsActive,
            user.LockedUntil,
            user.DeletedAt,
            user.CreatedAt,
            Shop = user.Shop is null ? null : CreateShopTarget(user.Shop)
        };
    }

    private async Task<object> GetCommentTargetDetailsAsync(Guid commentId, CancellationToken cancellationToken)
    {
        var comment = await _dbContext.MediaComments.AsNoTracking()
            .Include(item => item.User).ThenInclude(user => user.Shop)
            .Include(item => item.Media)
            .SingleOrDefaultAsync(item => item.Id == commentId, cancellationToken)
            ?? throw new NotFoundException("Yorum bulunamadi.");

        return new
        {
            comment.Id,
            comment.MediaId,
            comment.ParentCommentId,
            comment.CommentText,
            comment.CreatedAt,
            comment.UpdatedAt,
            Author = new
            {
                comment.User.Id,
                comment.User.FullName,
                comment.User.Email,
                AvatarPublicUrl = GeneratePublicAssetUrl(comment.User.AvatarUrl),
                Shop = comment.User.Shop is null ? null : CreateShopTarget(comment.User.Shop)
            },
            Media = new
            {
                comment.Media.Id,
                comment.Media.Caption,
                ThumbnailPublicUrl = GeneratePublicAssetUrl(comment.Media.ThumbnailUrl)
            }
        };
    }

    private async Task ArchiveReportedProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await _dbContext.Products.SingleOrDefaultAsync(item => item.Id == productId, cancellationToken)
            ?? throw new NotFoundException("Urun bulunamadi.");
        product.IsActive = false;
        product.Status = ProductStatus.Archived;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Guid> ArchiveReportedCourseAsync(Guid targetId, CancellationToken cancellationToken)
    {
        var course = await _dbContext.Courses.Include(item => item.Product)
            .SingleOrDefaultAsync(item => item.Id == targetId, cancellationToken);
        if (course is not null)
        {
            course.Product.IsActive = false;
            course.Product.Status = ProductStatus.Archived;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return course.Product.Id;
        }

        await ArchiveReportedProductAsync(targetId, cancellationToken);
        return targetId;
    }

    private async Task DeactivateReportedMediaAsync(Guid mediaId, CancellationToken cancellationToken)
    {
        var media = await _dbContext.Media.SingleOrDefaultAsync(item => item.Id == mediaId, cancellationToken)
            ?? throw new NotFoundException("Medya bulunamadi.");
        media.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SuspendReportedShopAsync(Guid adminUserId, Guid shopId, CancellationToken cancellationToken)
    {
        var shop = await _dbContext.Shops.Include(item => item.User)
            .SingleOrDefaultAsync(item => item.Id == shopId, cancellationToken)
            ?? throw new NotFoundException("Magaza bulunamadi.");
        await EnsureUserCanBeRestrictedAsync(adminUserId, shop.User, "Kendi magazanizi askiya alamazsiniz.", cancellationToken);
        shop.IsActive = false;
        shop.User.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SuspendReportedUserAsync(Guid adminUserId, Guid userId, CancellationToken cancellationToken)
    {
        var user = await GetUserForUpdateAsync(userId, cancellationToken);
        await EnsureUserCanBeRestrictedAsync(adminUserId, user, "Kendi hesabinizi askiya alamazsiniz.", cancellationToken);
        user.IsActive = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task DeleteReportedCommentAsync(Guid commentId, CancellationToken cancellationToken)
    {
        var comment = await _dbContext.MediaComments.SingleOrDefaultAsync(item => item.Id == commentId, cancellationToken)
            ?? throw new NotFoundException("Yorum bulunamadi.");
        _dbContext.MediaComments.Remove(comment);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private object CreateShopTarget(Shop shop) => new
    {
        shop.Id,
        shop.ShopName,
        shop.Slug,
        shop.IsActive,
        shop.IsVerified,
        ShopLogoPublicUrl = GeneratePublicAssetUrl(shop.LogoUrl)
    };

    private static object CreateOwnerTarget(User user) => new
    {
        UserId = user.Id,
        user.FullName,
        user.Email,
        user.IsActive,
        user.DeletedAt
    };

    private static ReportTargetOwner CreateOwner(User user, Shop? shop) => new(user.Id, user.FullName, user.Email, shop?.Id);

    private static bool IsFinalReportStatus(string status) =>
        string.Equals(status, "resolved", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "rejected", StringComparison.OrdinalIgnoreCase);

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

    private async Task<List<AdminReportDto>> QueryReportsAsync(string sql, object?[] parameters, CancellationToken cancellationToken)
    {
        var reports = await QueryAsync(
            sql,
            parameters,
            reader => new AdminReportRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8),
                reader.GetDateTime(9),
                reader.IsDBNull(5) ? null : reader.GetString(5)),
            cancellationToken);

        var result = new List<AdminReportDto>(reports.Count);
        foreach (var report in reports)
        {
            var owner = await ResolveReportOwnerAsync(report.Type, report.TargetId, cancellationToken);
            result.Add(new AdminReportDto(
                report.Id,
                report.Type,
                report.Type,
                report.TargetId,
                report.TargetTitle,
                owner.UserId,
                owner.Name,
                owner.Email,
                owner.ShopId,
                report.ReportedByUserId,
                report.ReportedByEmail,
                report.Reason,
                report.Description,
                report.Status,
                report.CreatedAt));
        }

        return result;
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

    private IQueryable<Order> GetSuccessfulOrderFinanceQuery(DateTime? startDate, DateTime? endDate)
    {
        var orders = _dbContext.Orders
            .AsNoTracking()
            .Where(order =>
                order.Status == OrderStatus.Completed &&
                order.Payment != null &&
                order.Payment.Status == PaymentStatusType.Succeeded);

        if (startDate.HasValue)
        {
            orders = orders.Where(order =>
                (order.Payment!.CreatedAt ?? order.CreatedAt) >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            orders = orders.Where(order =>
                (order.Payment!.CreatedAt ?? order.CreatedAt) <= endDate.Value);
        }

        return orders;
    }

    private IQueryable<SellerSubscriptionPayment> GetSuccessfulSubscriptionPaymentQuery(
        DateTime? startDate,
        DateTime? endDate)
    {
        var payments = _dbContext.SellerSubscriptionPayments
            .AsNoTracking()
            .Where(payment => payment.Status == "succeeded");

        if (startDate.HasValue)
        {
            payments = payments.Where(payment => payment.CreatedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            payments = payments.Where(payment => payment.CreatedAt <= endDate.Value);
        }

        return payments;
    }

    private static IQueryable<SellerSubscription> ApplySubscriptionStatusFilter(
        IQueryable<SellerSubscription> subscriptions,
        string? status,
        DateTime now)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return subscriptions;
        }

        return status.Trim().ToLowerInvariant() switch
        {
            "active" => subscriptions.Where(subscription =>
                subscription.Status == SubStatus.Active && subscription.CurrentPeriodEnd >= now),
            "expired" => subscriptions.Where(subscription =>
                subscription.Status == SubStatus.PastDue ||
                subscription.Status == SubStatus.Unpaid ||
                (subscription.Status == SubStatus.Active && subscription.CurrentPeriodEnd < now)),
            "cancelled" or "canceled" => subscriptions.Where(subscription => subscription.Status == SubStatus.Canceled),
            // Craftora does not currently sell a trial plan. Preserve the contract without
            // inventing trial records from normal subscriptions.
            "trial" => subscriptions.Where(_ => false),
            _ => throw new BadRequestException("Gecersiz abonelik durumu filtresi.")
        };
    }

    private static string GetFinanceSubscriptionStatus(SellerSubscription subscription, DateTime now)
    {
        if (subscription.Status == SubStatus.Canceled)
        {
            return "cancelled";
        }

        return subscription.Status == SubStatus.Active && subscription.CurrentPeriodEnd >= now
            ? "active"
            : "expired";
    }

    private static int CalculateRemainingDays(DateTime expiresAt, DateTime now, string status)
    {
        if (!string.Equals(status, "active", StringComparison.Ordinal) || expiresAt <= now)
        {
            return 0;
        }

        return Math.Max(0, (int)Math.Ceiling((expiresAt - now).TotalDays));
    }

    private static string GetFinanceShopStatus(Shop shop)
    {
        if (shop.User.DeletedAt is not null)
        {
            return "deleted";
        }

        return shop.IsActive == true && shop.User.IsActive == true
            ? "active"
            : "inactive";
    }

    private static int CalculateTotalPages(int totalCount, int pageSize)
    {
        return totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
    }

    private static void ValidateFinanceDateRange(DateTime? startDate, DateTime? endDate)
    {
        if (startDate.HasValue && endDate.HasValue && endDate.Value < startDate.Value)
        {
            throw new BadRequestException("Bitis tarihi baslangic tarihinden once olamaz.");
        }
    }

    private sealed record AdminReportRecord(
        Guid Id,
        string Type,
        Guid TargetId,
        string? TargetTitle,
        Guid? ReportedByUserId,
        string Reason,
        string? Description,
        string Status,
        DateTime CreatedAt,
        string? ReportedByEmail);

    private sealed record CompetitionScoreRow(
        Guid UserId,
        decimal Score,
        decimal SalesPoints,
        decimal ViewPoints,
        decimal EngagementPoints,
        decimal LearningPoints);

    private sealed record CompetitionRewardNotification(Guid UserId, string Title, string Body);

    private sealed record ReportTargetOwner(Guid? UserId, string? Name, string? Email, Guid? ShopId)
    {
        public static ReportTargetOwner Empty { get; } = new(null, null, null, null);
    }
}
