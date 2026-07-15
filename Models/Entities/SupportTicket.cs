using CraftoraApi.Models.Enums;

namespace CraftoraApi.Models.Entities;

public sealed class SupportTicket
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Subject { get; set; } = null!;

    public SupportTicketStatus Status { get; set; } = SupportTicketStatus.Open;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime LastMessageAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public Guid? ClosedByUserId { get; set; }

    public User User { get; set; } = null!;

    public User? ClosedByUser { get; set; }

    public ICollection<SupportTicketMessage> Messages { get; set; } = new List<SupportTicketMessage>();
}
