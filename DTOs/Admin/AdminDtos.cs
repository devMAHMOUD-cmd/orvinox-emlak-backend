using System.ComponentModel.DataAnnotations;

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

public sealed record AdminWarnUserRequestDto(
    [property: Required(ErrorMessage = "Uyari basligi zorunludur.")]
    [property: MinLength(2, ErrorMessage = "Uyari basligi en az 2 karakter olmalidir.")]
    string Title,

    [property: Required(ErrorMessage = "Uyari mesaji zorunludur.")]
    [property: MinLength(2, ErrorMessage = "Uyari mesaji en az 2 karakter olmalidir.")]
    string Message);

public sealed record AdminLockUserRequestDto(string Reason, DateTime Until);

public sealed record AdminSuspendUserRequestDto(
    [property: Required(ErrorMessage = "Askiya alma nedeni zorunludur.")]
    [property: StringLength(1000, MinimumLength = 2, ErrorMessage = "Askiya alma nedeni 2 ile 1000 karakter arasynda olmalidir.")]
    string Reason);

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
    [property: Required(ErrorMessage = "Yarisma basligi zorunludur.")]
    [property: StringLength(200, MinimumLength = 3, ErrorMessage = "Yarisma basligi 3 ile 200 karakter arasinda olmalidir.")]
    string Title,

    [property: StringLength(4000, ErrorMessage = "Yarisma aciklamasi en fazla 4000 karakter olabilir.")]
    string? Description,

    [property: Required(ErrorMessage = "Baslangic tarihi zorunludur.")]
    DateTime StartDate,

    [property: Required(ErrorMessage = "Bitis tarihi zorunludur.")]
    DateTime EndDate,
    bool RewardsHidden,

    [property: StringLength(255, ErrorMessage = "Odul havuzu en fazla 255 karakter olabilir.")]
    string? PrizePool,

    [property: Required(ErrorMessage = "Yarisma durumu zorunludur.")]
    [property: RegularExpression("^(draft|active)$", ErrorMessage = "Yarisma durumu draft veya active olmalidir.")]
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
    [property: Required(ErrorMessage = "Pulse haber basligi zorunludur.")]
    [property: MinLength(2, ErrorMessage = "Pulse haber basligi en az 2 karakter olmalidir.")]
    string Title,
    string? Description,
    string? Meta,

    [property: StringLength(100, ErrorMessage = "Icon en fazla 100 karakter olabilir.")]
    string? Icon,
    bool IsPublished,
    DateTime? IsNewUntil);

public sealed record HomeCardsDto(
    [property: Required(ErrorMessage = "En az bir ana sayfa karti zorunludur.")]
    [property: MinLength(1, ErrorMessage = "En az bir ana sayfa karti zorunludur.")]
    IReadOnlyList<HomeCardDto> Cards);

public sealed record HomeCardDto(
    [property: Required(ErrorMessage = "Kart kimligi zorunludur.")]
    [property: StringLength(100, MinimumLength = 1, ErrorMessage = "Kart kimligi en fazla 100 karakter olabilir.")]
    [property: RegularExpression("^[A-Za-z0-9_-]+$", ErrorMessage = "Kart kimligi sadece harf, rakam, alt cizgi ve tire icerebilir.")]
    string Id,

    [property: Required(ErrorMessage = "Kart basligi zorunludur.")]
    [property: MinLength(2, ErrorMessage = "Kart basligi en az 2 karakter olmalidir.")]
    string Title,
    string? Description,

    [property: StringLength(100, ErrorMessage = "Kart ikonu en fazla 100 karakter olabilir.")]
    string? Icon,

    [property: StringLength(100, ErrorMessage = "Kart aksiyon tipi en fazla 100 karakter olabilir.")]
    string? ActionType,

    [property: Range(0, 1000, ErrorMessage = "Kart sirasi 0 ile 1000 arasynda olmalidir.")]
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
