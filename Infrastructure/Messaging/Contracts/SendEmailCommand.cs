namespace CraftoraApi.Infrastructure.Messaging.Contracts;

public sealed record SendEmailCommand(
    string To,
    string Subject,
    string Body,
    bool IsHtml);
