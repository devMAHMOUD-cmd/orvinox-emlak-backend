using CraftoraApi.Models.Enums;

namespace CraftoraApi.Models.Entities;

public sealed class SupportTicketMessage
{
    public Guid Id { get; set; }

    public Guid TicketId { get; set; }

    public Guid SenderId { get; set; }

    public SupportMessageSenderRole SenderRole { get; set; }

    public string Message { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public SupportTicket Ticket { get; set; } = null!;

    public User Sender { get; set; } = null!;
}
