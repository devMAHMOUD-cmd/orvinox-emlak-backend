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

public sealed record AdminFinanceOverviewDto(
    decimal GrossSales,
    decimal PlatformCommissionRate,
    decimal CommissionRevenue,
    decimal SubscriptionRevenue,
    bool HistoricalRevenueAvailable,
    decimal TotalPlatformRevenue,
    int TotalOrders,
    int ActiveSubscriptions,
    int ExpiringSubscriptions);

public sealed record AdminCommissionListItemDto(
    Guid OrderId,
    string OrderNumber,
    Guid SellerId,
    Guid ShopId,
    string ShopName,
    Guid ProductId,
    string ProductTitle,
    decimal GrossAmount,
    decimal CommissionRate,
    decimal PlatformFee,
    decimal SellerEarnings,
    string Currency,
    string PaymentStatus,
    DateTime? CreatedAt);

public sealed record AdminSubscriptionFinanceListItemDto(
    Guid SubscriptionId,
    Guid UserId,
    Guid ShopId,
    string ShopName,
    string? OwnerName,
    string OwnerEmail,
    string PlanName,
    decimal Amount,
    string Currency,
    string Status,
    string ShopStatus,
    DateTime? StartedAt,
    DateTime ExpiresAt,
    int RemainingDays,
    DateTime? LastPaymentAt);

public sealed record AdminUserListItemDto(
    Guid Id,
    string Email,
    string? FullName,
    string Role,
    string Status,
    Guid? ShopId,
    string? ShopName,
    string? ShopLogoPublicUrl,
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
    string TargetType,
    Guid TargetId,
    string? TargetTitle,
    Guid? TargetOwnerUserId,
    string? TargetOwnerName,
    string? TargetOwnerEmail,
    Guid? TargetShopId,
    Guid? ReportedByUserId,
    string? ReportedByEmail,
    string Reason,
    string? Description,
    string Status,
    DateTime CreatedAt);

public sealed record AdminReportTargetDto(
    Guid ReportId,
    string TargetType,
    Guid TargetId,
    Guid? TargetOwnerUserId,
    string? TargetOwnerName,
    string? TargetOwnerEmail,
    Guid? TargetShopId,
    object Target);

public sealed record AdminBlockReportTargetRequestDto(
    [property: Required(ErrorMessage = "Engelleme nedeni zorunludur.")]
    [property: StringLength(1000, MinimumLength = 2, ErrorMessage = "Engelleme nedeni 2 ile 1000 karakter arasinda olmalidir.")]
    string Reason);

public sealed record AdminCompetitionDto(
    Guid Id,
    string Title,
    string? Description,
    DateTime StartDate,
    DateTime EndDate,
    bool RewardsHidden,
    string? PrizePool,
    string Status,
    bool IsActive,
    bool RewardsDistributed,
    int RewardedCount);

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
    [property: Required(ErrorMessage = "En az bir kazanan gereklidir.")]
    [property: MinLength(1, ErrorMessage = "En az bir kazanan gereklidir.")]
    IReadOnlyList<AdminRewardWinnerDto> Winners);

public sealed record AdminRewardWinnerDto(
    Guid UserId,
    [property: Range(1, int.MaxValue, ErrorMessage = "Derece 1 veya daha buyuk olmalidir.")]
    int Rank,
    [property: Required(ErrorMessage = "Odul tipi zorunludur.")]
    [property: RegularExpression("^(money|premium_1_month|certificate)$", ErrorMessage = "Odul tipi money, premium_1_month veya certificate olmalidir.")]
    string RewardType,
    decimal? Amount,
    string? Currency,
    string? Note);

public sealed record AdminCompetitionLeaderboardResponseDto(
    Guid CompetitionId,
    string Status,
    IReadOnlyList<AdminCompetitionLeaderboardItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record AdminCompetitionLeaderboardItemDto(
    int Rank,
    Guid UserId,
    string? DisplayName,
    string? AvatarPublicUrl,
    Guid? ShopId,
    string? ShopName,
    string? ShopLogoPublicUrl,
    decimal Score,
    decimal SalesPoints,
    decimal ViewPoints,
    decimal EngagementPoints,
    decimal LearningPoints);

public sealed record AdminCompetitionParticipantsResponseDto(
    Guid CompetitionId,
    IReadOnlyList<AdminCompetitionParticipantDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record AdminCompetitionParticipantDto(
    Guid UserId,
    string? DisplayName,
    string? AvatarPublicUrl,
    Guid? ShopId,
    string? ShopName,
    string? ShopLogoPublicUrl,
    DateTime? JoinedAt,
    decimal Score,
    int? Rank);

public sealed record CompetitionCertificateData(
    string CompetitionTitle,
    string RecipientName,
    int Rank,
    DateTime IssuedAt);

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
