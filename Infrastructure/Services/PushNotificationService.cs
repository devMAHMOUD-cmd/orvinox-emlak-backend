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
            _logger.LogInformation("No active device token found. UserId: {UserId}", userId);
            return;
        }

        if (!EnsureFirebaseApp())
        {
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

        var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(
            message,
            cancellationToken);

        _logger.LogInformation(
            "Push notification sent. UserId: {UserId}, Success: {SuccessCount}, Failure: {FailureCount}",
            userId,
            response.SuccessCount,
            response.FailureCount);
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
