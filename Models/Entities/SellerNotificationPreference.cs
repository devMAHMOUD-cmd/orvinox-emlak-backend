namespace CraftoraApi.Models.Entities;

public sealed class SellerNotificationPreference
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public bool OrderEmails { get; set; } = true;

    public bool WeeklyReportEmails { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
