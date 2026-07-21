namespace CraftoraApi.Models.Entities;

public sealed class AdminCompetitionReward
{
    public Guid Id { get; set; }
    public Guid ContestId { get; set; }
    public Guid UserId { get; set; }
    public int Rank { get; set; }
    public string RewardType { get; set; } = null!;
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? Note { get; set; }
    public string? CertificateUrl { get; set; }
    public DateTime? CreatedAt { get; set; }
}
