namespace CraftoraApi.DTOs.Admin;

public sealed record AdminPagedResponseDto<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record AdminOverviewDto(
    int TotalUsers,
    int TotalSellers,
    int TotalShops,
    int TotalProducts,
    int TotalCourses,
    int TotalMedia,
    int TotalOrders,
    decimal GrossRevenue,
    int PendingReports,
    int ActiveCompetitions,
    int NewUsersToday,
    int OrdersToday);

public sealed record AdminUserListItemDto(
    Guid Id,
    string Email,
    string? FullName,
    string Role,
    string Status,
    Guid? ShopId,
    string? ShopName,
    string? AvatarUrl,
    DateTime? CreatedAt,
    DateTime? LastLoginAt,
    int ProductCount,
    int CourseCount,
    int MediaCount,
    int OrderCount,
    int ReportCount,
    decimal TotalXp);

public sealed record AdminUserDetailDto(
    AdminUserListItemDto User,
    AdminShopSummaryDto? Shop,
    IReadOnlyList<AdminProductSummaryDto> Products,
    IReadOnlyList<AdminProductSummaryDto> Courses,
    IReadOnlyList<AdminMediaSummaryDto> Media,
    IReadOnlyList<AdminOrderSummaryDto> Orders,
    IReadOnlyList<AdminReportDto> Reports,
    IReadOnlyList<AdminWarningDto> Warnings,
    object Gamification);

public sealed record AdminShopSummaryDto(
    Guid Id,
    string ShopName,
    string Slug,
    bool IsActive,
    bool IsVerified,
    DateTime? CreatedAt);

public sealed record AdminProductSummaryDto(
    Guid Id,
    string Title,
    string Type,
    string Status,
    bool IsActive,
    decimal Price,
    DateTime? CreatedAt);

public sealed record AdminMediaSummaryDto(
    Guid Id,
    string? Caption,
    string Status,
    bool IsActive,
    int ViewCount,
    int LikeCount,
    DateTime? CreatedAt);

public sealed record AdminOrderSummaryDto(
    Guid Id,
    string OrderNumber,
    decimal Amount,
    string? Currency,
    string Status,
    DateTime? CreatedAt);

public sealed record AdminWarningDto(
    Guid Id,
    Guid UserId,
    Guid? AdminUserId,
    string Title,
    string Message,
    DateTime CreatedAt);

public sealed record AdminWarnUserRequestDto(string Title, string Message);

public sealed record AdminLockUserRequestDto(string Reason, DateTime Until);

public sealed record AdminSuspendUserRequestDto(string Reason);

public sealed record AdminReportDto(
    Guid Id,
    string Type,
    Guid TargetId,
    string? TargetTitle,
    Guid? ReportedByUserId,
    string? ReportedByEmail,
    string Reason,
    string? Description,
    string Status,
    DateTime CreatedAt);

public sealed record AdminCompetitionDto(
    Guid Id,
    string Title,
    string? Description,
    DateTime StartDate,
    DateTime EndDate,
    bool RewardsHidden,
    string? PrizePool,
    string Status,
    bool IsActive);

public sealed record AdminUpsertCompetitionDto(
    string Title,
    string? Description,
    DateTime StartDate,
    DateTime EndDate,
    bool RewardsHidden,
    string? PrizePool,
    string Status);

public sealed record AdminDistributeRewardsRequestDto(
    IReadOnlyList<AdminRewardWinnerDto> Winners);

public sealed record AdminRewardWinnerDto(
    Guid UserId,
    int Rank,
    string RewardType,
    decimal? Amount,
    string? Currency,
    string? Note);

public sealed record PulseNewsDto(
    Guid Id,
    string Title,
    string? Description,
    string? Meta,
    string? Icon,
    bool IsPublished,
    DateTime? IsNewUntil,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record UpsertPulseNewsDto(
    string Title,
    string? Description,
    string? Meta,
    string? Icon,
    bool IsPublished,
    DateTime? IsNewUntil);

public sealed record HomeCardsDto(IReadOnlyList<HomeCardDto> Cards);

public sealed record HomeCardDto(
    string Id,
    string Title,
    string? Description,
    string? Icon,
    string? ActionType,
    int SortOrder,
    bool IsActive);

public sealed record AdminAuditLogDto(
    Guid Id,
    Guid? AdminUserId,
    string? AdminEmail,
    string Action,
    string TargetType,
    Guid? TargetId,
    string Metadata,
    DateTime CreatedAt);
