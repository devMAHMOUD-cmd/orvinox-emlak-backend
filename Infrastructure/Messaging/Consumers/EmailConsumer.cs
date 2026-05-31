using CraftoraApi.Infrastructure.Messaging.Contracts;
using CraftoraApi.Infrastructure.Services;
using MassTransit;

namespace CraftoraApi.Infrastructure.Messaging.Consumers;

public sealed class EmailConsumer : IConsumer<SendEmailCommand>
{
    private readonly IEmailService _emailService;
    private readonly ILogger<EmailConsumer> _logger;

    public EmailConsumer(
        IEmailService emailService,
        ILogger<EmailConsumer> logger)
    {
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Consume(ConsumeContext<SendEmailCommand> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Email command received. MessageId: {MessageId}, CorrelationId: {CorrelationId}, To: {To}, Subject: {Subject}",
            context.MessageId,
            context.CorrelationId,
            message.To,
            message.Subject);

        try
        {
            await _emailService.SendEmailAsync(
                message.To,
                message.Subject,
                message.Body,
                message.IsHtml,
                context.CancellationToken);

            _logger.LogInformation(
                "Email command consumed successfully. MessageId: {MessageId}, To: {To}",
                context.MessageId,
                message.To);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Email command failed. MessageId: {MessageId}, To: {To}, Subject: {Subject}",
                context.MessageId,
                message.To,
                message.Subject);

            throw;
        }
    }
}
