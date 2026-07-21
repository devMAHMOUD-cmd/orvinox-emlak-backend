namespace CraftoraApi.DTOs.Seller;

public sealed record SellerNotificationPreferencesDto(
    bool OrderEmails,
    bool WeeklyReportEmails);

public sealed record TestSellerEmailResponseDto(
    string Message);

public sealed record WeeklySellerReportPreviewRequestDto(
    DateTime? StartDate,
    DateTime? EndDate);

public sealed record WeeklySellerReportPreviewResponseDto(
    string Message,
    string DownloadUrl,
    string FileName,
    DateTime ExpiresAt);
