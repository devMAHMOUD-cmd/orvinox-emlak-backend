namespace CraftoraApi.Models.Entities;

public sealed class AdminEmailCampaignRecipient
{
    public Guid Id { get; set; }

    public Guid CampaignId { get; set; }

    public Guid UserId { get; set; }

    public string Email { get; set; } = null!;

    public string? FullName { get; set; }

    public string Status { get; set; } = "pending";

    public int AttemptCount { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? SentAt { get; set; }

    public AdminEmailCampaign Campaign { get; set; } = null!;
}
