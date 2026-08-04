namespace CraftoraApi.Models.Entities;

public sealed class SellerNotificationPreference
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public bool OrderEmails { get; set; } = true;

    public bool WeeklyReportEmails { get; set; } = true;

    public bool OrderNotifications { get; set; } = true;

    public bool LikeNotifications { get; set; } = true;

    public bool CommentNotifications { get; set; } = true;

    public bool FollowNotifications { get; set; } = true;

    public bool NewContentNotifications { get; set; } = true;

    public bool QuestionAnswerNotifications { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
