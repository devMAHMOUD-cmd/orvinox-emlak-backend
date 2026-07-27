namespace CraftoraApi.Models.Entities;

public sealed class AdminEmailCampaign
{
    public Guid Id { get; set; }

    public Guid AdminUserId { get; set; }

    public string IdempotencyKey { get; set; } = null!;

    public string Audience { get; set; } = null!;

    public string Subject { get; set; } = null!;

    public string Message { get; set; } = null!;

    public string Status { get; set; } = "queued";

    public int RecipientCount { get; set; }

    public int SentCount { get; set; }

    public int FailedCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public ICollection<AdminEmailCampaignRecipient> Recipients { get; set; } =
        new List<AdminEmailCampaignRecipient>();
}
