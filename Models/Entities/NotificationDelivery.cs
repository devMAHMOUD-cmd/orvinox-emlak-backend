using System;
using System.Collections.Generic;

namespace CraftoraApi.Models.Entities;

public partial class NotificationDelivery
{
    public Guid Id { get; set; }

    public Guid NotificationId { get; set; }

    public string Channel { get; set; } = null!;

    public string? Status { get; set; }

    public string? Provider { get; set; }

    public string? ProviderMessageId { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime? SentAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Notification Notification { get; set; } = null!;
}
