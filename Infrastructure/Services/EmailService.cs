using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CraftoraApi.Configuration;
using Microsoft.Extensions.Options;

namespace CraftoraApi.Infrastructure.Services;

public sealed class EmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        IOptions<EmailSettings> options,
        IHttpClientFactory httpClientFactory,
        ILogger<EmailService> logger)
    {
        _settings = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
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

        _logger.LogInformation(
            "Email send requested. Provider: Resend, To: {To}, Subject: {Subject}",
            to,
            subject);

        await SendWithResendAsync(to, subject, body, isHtml, cancellationToken);
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
            throw new InvalidOperationException("Resend API key is missing.");
        }

        if (string.IsNullOrWhiteSpace(_settings.FromEmail))
        {
            throw new InvalidOperationException("Email FromEmail is missing for Resend.");
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

}
