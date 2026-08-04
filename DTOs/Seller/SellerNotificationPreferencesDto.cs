namespace CraftoraApi.DTOs.Seller;

public sealed record SellerNotificationPreferencesDto(
    bool OrderEmails,
    bool WeeklyReportEmails,
    bool OrderNotifications,
    bool LikeNotifications,
    bool CommentNotifications,
    bool FollowNotifications,
    bool NewContentNotifications,
    bool QuestionAnswerNotifications);

public sealed record UpdateSellerNotificationPreferencesDto(
    bool? OrderEmails,
    bool? WeeklyReportEmails,
    bool? OrderNotifications,
    bool? LikeNotifications,
    bool? CommentNotifications,
    bool? FollowNotifications,
    bool? NewContentNotifications,
    bool? QuestionAnswerNotifications);

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
