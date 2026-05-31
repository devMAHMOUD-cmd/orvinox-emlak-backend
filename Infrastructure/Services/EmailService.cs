using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CraftoraApi.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace CraftoraApi.Infrastructure.Services;

public sealed class EmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISendGridClient _sendGridClient;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        IOptions<EmailSettings> options,
        IHttpClientFactory httpClientFactory,
        ISendGridClient sendGridClient,
        ILogger<EmailService> logger)
    {
        _settings = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _sendGridClient = sendGridClient ?? throw new ArgumentNullException(nameof(sendGridClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SendEmailAsync(
        string to,
        string subject,
        string body,
        bool isHtml,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        var provider = string.IsNullOrWhiteSpace(_settings.Provider)
            ? "resend"
            : _settings.Provider.Trim().ToLowerInvariant();

        _logger.LogInformation(
            "Email send requested. Provider: {Provider}, To: {To}, Subject: {Subject}",
            provider,
            to,
            subject);

        switch (provider)
        {
            case "resend":
                await SendWithResendAsync(to, subject, body, isHtml, cancellationToken);
                break;
            case "sendgrid":
                await SendWithSendGridAsync(to, subject, body, isHtml, cancellationToken);
                break;
            case "smtp":
                await SendWithSmtpAsync(to, subject, body, isHtml, cancellationToken);
                break;
            default:
                _logger.LogWarning(
                    "Unknown email provider configured. Email delivery skipped. Provider: {Provider}, To: {To}, Subject: {Subject}",
                    _settings.Provider,
                    to,
                    subject);
                break;
        }
    }

    private async Task SendWithResendAsync(
        string to,
        string subject,
        string body,
        bool isHtml,
        CancellationToken cancellationToken)
    {
        var apiKey = _settings.Resend.ApiKey ?? _settings.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "Resend API key is missing. Email delivery skipped. To: {To}, Subject: {Subject}",
                to,
                subject);
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.FromEmail))
        {
            _logger.LogWarning(
                "Email FromEmail is missing. Email delivery skipped. Provider: Resend, To: {To}, Subject: {Subject}",
                to,
                subject);
            return;
        }

        var payload = new Dictionary<string, object?>
        {
            ["from"] = string.IsNullOrWhiteSpace(_settings.FromName)
                ? _settings.FromEmail
                : $"{_settings.FromName} <{_settings.FromEmail}>",
            ["to"] = new[] { to },
            ["subject"] = subject,
            [isHtml ? "html" : "text"] = body
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        _logger.LogInformation(
            "Resend email send started. From: {FromEmail}, FromName: {FromName}, To: {To}, Subject: {Subject}",
            _settings.FromEmail,
            _settings.FromName,
            to,
            subject);

        try
        {
            var client = _httpClientFactory.CreateClient("Resend");
            using var response = await client.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Resend email sent. StatusCode: {StatusCode}, From: {FromEmail}, To: {To}, Subject: {Subject}",
                    response.StatusCode,
                    _settings.FromEmail,
                    to,
                    subject);
                return;
            }

            _logger.LogError(
                "Resend email send failed. StatusCode: {StatusCode}, ResponseBody: {ResponseBody}, From: {FromEmail}, To: {To}, Subject: {Subject}",
                response.StatusCode,
                responseBody,
                _settings.FromEmail,
                to,
                subject);

            throw new InvalidOperationException($"Resend email send failed with status code {response.StatusCode}.");
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Resend email send failed with exception. From: {FromEmail}, To: {To}, Subject: {Subject}",
                _settings.FromEmail,
                to,
                subject);

            throw;
        }
    }

    private async Task SendWithSendGridAsync(
        string to,
        string subject,
        string body,
        bool isHtml,
        CancellationToken cancellationToken)
    {
        var apiKey = _settings.SendGrid.ApiKey ?? _settings.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "SendGrid API key is missing. Email delivery skipped. To: {To}, Subject: {Subject}",
                to,
                subject);
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.FromEmail))
        {
            _logger.LogWarning(
                "Email FromEmail is missing. Email delivery skipped. Provider: SendGrid, To: {To}, Subject: {Subject}",
                to,
                subject);
            return;
        }

        var from = new EmailAddress(_settings.FromEmail, _settings.FromName);
        var message = new SendGridMessage();
        message.SetFrom(from);
        message.AddTo(new EmailAddress(to));
        message.SetSubject(subject);

        if (isHtml)
        {
            message.AddContent(MimeType.Html, body);
        }
        else
        {
            message.AddContent(MimeType.Text, body);
        }

        _logger.LogInformation(
            "SendGrid email send started. From: {FromEmail}, FromName: {FromName}, To: {To}, Subject: {Subject}",
            _settings.FromEmail,
            _settings.FromName,
            to,
            subject);

        try
        {
            var response = await _sendGridClient.SendEmailAsync(message, cancellationToken);
            var responseBody = response.Body is null
                ? string.Empty
                : await response.Body.ReadAsStringAsync(cancellationToken);

            if ((int)response.StatusCode is >= 200 and <= 299)
            {
                _logger.LogInformation(
                    "SendGrid email sent. StatusCode: {StatusCode}, From: {FromEmail}, To: {To}, Subject: {Subject}",
                    response.StatusCode,
                    _settings.FromEmail,
                    to,
                    subject);
                return;
            }

            _logger.LogError(
                "SendGrid email send failed. StatusCode: {StatusCode}, ResponseBody: {ResponseBody}, From: {FromEmail}, To: {To}, Subject: {Subject}",
                response.StatusCode,
                responseBody,
                _settings.FromEmail,
                to,
                subject);

            throw new InvalidOperationException($"SendGrid email send failed with status code {response.StatusCode}.");
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "SendGrid email send failed with exception. From: {FromEmail}, To: {To}, Subject: {Subject}",
                _settings.FromEmail,
                to,
                subject);

            throw;
        }
    }

    private async Task SendWithSmtpAsync(
        string to,
        string subject,
        string body,
        bool isHtml,
        CancellationToken cancellationToken)
    {
        var host = _settings.Smtp.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogWarning(
                "SMTP host is missing. Email delivery skipped. To: {To}, Subject: {Subject}",
                to,
                subject);
            return;
        }

        var message = new MimeMessage();
        var fromEmail = _settings.FromEmail;
        var fromName = _settings.FromName;

        if (string.IsNullOrWhiteSpace(fromEmail))
        {
            _logger.LogWarning(
                "Email FromEmail is missing. Email delivery skipped. Provider: SMTP, To: {To}, Subject: {Subject}",
                to,
                subject);
            return;
        }

        message.From.Add(new MailboxAddress(fromName, fromEmail));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart(isHtml ? "html" : "plain")
        {
            Text = body
        };

        var port = _settings.Smtp.Port <= 0 ? 587 : _settings.Smtp.Port;
        var username = _settings.Smtp.Username;
        var password = _settings.Smtp.Password;
        var secureSocketOptions = _settings.Smtp.UseSsl
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTlsWhenAvailable;

        _logger.LogInformation(
            "SMTP email send started. Host: {Host}, Port: {Port}, From: {FromEmail}, FromName: {FromName}, To: {To}, Subject: {Subject}",
            host,
            port,
            fromEmail,
            fromName,
            to,
            subject);

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, secureSocketOptions, cancellationToken);

            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                await client.AuthenticateAsync(username, password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);

            _logger.LogInformation(
                "SMTP email sent. From: {FromEmail}, To: {To}, Subject: {Subject}",
                fromEmail,
                to,
                subject);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "SMTP email send failed. Host: {Host}, Port: {Port}, From: {FromEmail}, To: {To}, Subject: {Subject}",
                host,
                port,
                fromEmail,
                to,
                subject);

            throw;
        }
    }
}
