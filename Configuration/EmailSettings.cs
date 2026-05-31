namespace CraftoraApi.Configuration;

public sealed class EmailSettings
{
    public string Provider { get; set; } = "resend";
    public string? ApiKey { get; set; }
    public string FromEmail { get; set; } = "onboarding@resend.dev";
    public string FromName { get; set; } = "Craftora";
    public ResendEmailSettings Resend { get; set; } = new();
    public SendGridEmailSettings SendGrid { get; set; } = new();
    public SmtpEmailSettings Smtp { get; set; } = new();
}

public sealed class ResendEmailSettings
{
    public string? ApiKey { get; set; }
}

public sealed class SendGridEmailSettings
{
    public string? ApiKey { get; set; }
}

public sealed class SmtpEmailSettings
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool UseSsl { get; set; }
}
