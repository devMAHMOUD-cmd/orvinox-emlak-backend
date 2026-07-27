using System.Data;
using System.Data.Common;
using CraftoraApi.Data;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;

namespace CraftoraApi.Infrastructure.Services;

public sealed class PushNotificationService : IPushNotificationService
{
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PushNotificationService> _logger;

    public PushNotificationService(
        AppDbContext dbContext,
        IConfiguration configuration,
        ILogger<PushNotificationService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SendPushNotificationAsync(
        Guid notificationId,
        Guid userId,
        string title,
        string body,
        Dictionary<string, string> data,
        CancellationToken cancellationToken = default)
    {
        var tokens = await _dbContext.UserDeviceTokens
            .AsNoTracking()
            .Where(token => token.UserId == userId && token.IsActive == true)
            .Select(token => token.Token)
            .ToListAsync(cancellationToken);

        if (tokens.Count == 0)
        {
            await RecordDeliveryAsync(
                notificationId,
                "skipped",
                "firebase",
                "No active device token.",
                cancellationToken);
            _logger.LogInformation("No active device token found. UserId: {UserId}", userId);
            return;
        }

        if (!EnsureFirebaseApp())
        {
            await RecordDeliveryAsync(
                notificationId,
                "mocked",
                "firebase",
                null,
                cancellationToken);
            _logger.LogInformation(
                "Firebase app is not configured. Push notification mocked. UserId: {UserId}, Title: {Title}",
                userId,
                title);
            return;
        }

        var message = new MulticastMessage
        {
            Tokens = tokens,
            Notification = new Notification
            {
                Title = title,
                Body = body
            },
            Data = data
        };

        BatchResponse response;
        try
        {
            response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(
                message,
                cancellationToken);
        }
        catch (Exception exception)
        {
            await RecordDeliveryAsync(
                notificationId,
                "failed",
                "firebase",
                exception.Message,
                cancellationToken);
            throw;
        }

        var status = response.FailureCount == 0
            ? "sent"
            : response.SuccessCount == 0
                ? "failed"
                : "partial";
        await RecordDeliveryAsync(
            notificationId,
            status,
            "firebase",
            response.FailureCount == 0
                ? null
                : $"{response.FailureCount} device delivery failed.",
            cancellationToken);

        _logger.LogInformation(
            "Push notification sent. UserId: {UserId}, Success: {SuccessCount}, Failure: {FailureCount}",
            userId,
            response.SuccessCount,
            response.FailureCount);
    }

    private async Task RecordDeliveryAsync(
        Guid notificationId,
        string status,
        string provider,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await _dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT public.record_notification_delivery(
                    CAST(@notification_id AS uuid),
                    CAST(@status AS varchar),
                    CAST(@provider AS varchar),
                    CAST(@error_message AS text))
                """;
            AddParameter(command, "notification_id", notificationId);
            AddParameter(command, "status", status);
            AddParameter(command, "provider", provider);
            AddParameter(command, "error_message", errorMessage);
            await command.ExecuteScalarAsync(cancellationToken);
        }
        finally
        {
            if (openedHere)
            {
                await _dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private bool EnsureFirebaseApp()
    {
        if (FirebaseApp.DefaultInstance is not null)
        {
            return true;
        }

        var credentialsPath = _configuration["Firebase:CredentialsPath"];
        var projectId = _configuration["Firebase:ProjectId"];

        if (string.IsNullOrWhiteSpace(credentialsPath) || !File.Exists(credentialsPath))
        {
            _logger.LogInformation("Firebase credentials are missing. Push notification is mocked.");
            return false;
        }

        #pragma warning disable CS0618
        var credential = GoogleCredential.FromFile(credentialsPath);
        #pragma warning restore CS0618

        FirebaseApp.Create(new AppOptions
        {
            Credential = credential,
            ProjectId = projectId
        });

        return true;
    }
}
