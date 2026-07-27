namespace CraftoraApi.DTOs.Admin;

public sealed record AdminEmailCampaignPreviewRequestDto(
    string Audience,
    string Subject,
    string Message);

public sealed record AdminEmailCampaignSendRequestDto(
    string Audience,
    string Subject,
    string Message,
    string IdempotencyKey);

public sealed record AdminEmailCampaignPreviewDto(
    string Audience,
    int RecipientCount,
    IReadOnlyList<string> SampleRecipients,
    string Subject,
    string HtmlBody);

public sealed record AdminEmailCampaignDto(
    Guid Id,
    string Audience,
    string Subject,
    string Status,
    int RecipientCount,
    int SentCount,
    int FailedCount,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt);
