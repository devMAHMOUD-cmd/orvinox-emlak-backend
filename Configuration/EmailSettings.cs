namespace CraftoraApi.Configuration;

public sealed class EmailSettings
{
    public string Provider { get; set; } = "resend";
    public string? ApiKey { get; set; }
    public string FromEmail { get; set; } = "onboarding@resend.dev";
    public string FromName { get; set; } = "Craftora";
    public ResendEmailSettings Resend { get; set; } = new();
}

public sealed class ResendEmailSettings
{
    public string? ApiKey { get; set; }
}
