using System.Net;
using CraftoraApi.Models.Enums;

namespace CraftoraApi.Models.Entities;

public partial class AnalyticsEvent
{
    public Guid Id { get; set; }

    public Guid ShopId { get; set; }

    public Guid? ProductId { get; set; }

    public Guid? UserId { get; set; }

    public Guid? OrderId { get; set; }

    public AnalyticsEventType EventType { get; set; }

    public string? SessionId { get; set; }

    public string? Source { get; set; }

    public string? Referrer { get; set; }

    public string? UtmSource { get; set; }

    public string? UtmMedium { get; set; }

    public string? UtmCampaign { get; set; }

    public string? DeviceType { get; set; }

    public IPAddress? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? Metadata { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Shop Shop { get; set; } = null!;

    public virtual Product? Product { get; set; }

    public virtual User? User { get; set; }

    public virtual Order? Order { get; set; }
}
