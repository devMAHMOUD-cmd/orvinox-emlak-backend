namespace CraftoraApi.Configuration;

public sealed class ResendInboundSettings
{
    public string? ApiKey { get; set; }

    public string? WebhookSecret { get; set; }

    public string SupportAddress { get; set; } = "support@craftoramedya.com";
}
