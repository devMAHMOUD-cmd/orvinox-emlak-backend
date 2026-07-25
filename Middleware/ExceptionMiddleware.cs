using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Serilog;

namespace CraftoraApi.Middleware;

/// <summary>
/// Global exception handler middleware
/// Pipeline'daki TÜM hataları yakalar ve düzgün JSON response döndürür
/// Development ve Production'da farklı davranır (stack trace gizleme)
/// </summary>
/// 
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;

    public ExceptionMiddleware(
        RequestDelegate next,
        ILogger<ExceptionMiddleware> logger,
        IWebHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            // Pipeline'ı devam ettir
            await _next(context);
        }
        catch (Exception ex)
        {
            // Hata yakalandı - işle
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Response body'ye yazmaya başladıysa, hata response gönderilemez
        if (context.Response.HasStarted)
        {
            _logger.LogWarning("Response başlamış, exception handle edilemiyor");
            return;
        }

        // Response headers'ı ayarla
        context.Response.ContentType = "application/json; charset=utf-8";
        if (exception is AccountLockedException accountLockedException)
        {
            context.Response.StatusCode = accountLockedException.StatusCode;
            var lockedResponse = new
            {
                code = accountLockedException.ErrorCode,
                message = accountLockedException.Message,
                reason = accountLockedException.Reason,
                lockedUntil = accountLockedException.LockedUntil.ToString("O")
            };

            var lockedJson = JsonSerializer.Serialize(
                lockedResponse,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await context.Response.WriteAsync(lockedJson);
            return;
        }

        // Request bilgilerini çıkar
        var requestId = context.Request.HttpContext.TraceIdentifier;
        var path = context.Request.Path.ToString();
        var method = context.Request.Method;
        var userId = context.User.FindFirst("sub")?.Value ?? "anonymous";

        // Exception'ı tipi ve status code'u belirle
        var (statusCode, errorCode, message) = GetErrorDetails(exception);

        // Special cases
        HandleSpecialHeaders(context, exception, statusCode);

        // Development ortamında debug info ekle
        var debugInfo = _environment.IsDevelopment()
            ? new
            {
                exceptionType = exception.GetType().Name,
                stackTrace = exception.StackTrace
            }
            : null;

        // Response body'sini oluştur
        var response = new
        {
            error = new
            {
                code = errorCode,
                message = GetSafeMessage(message, exception, statusCode),
                statusCode = statusCode,
                timestamp = DateTime.UtcNow.ToString("O"),
                path = path,
                method = method,
                requestId = requestId,
                
                // ValidationException için errors
                errors = exception is ValidationException validationEx
                    ? validationEx.Errors
                    : null,

                // Development için debug info
                debug = debugInfo
            }
        };

        // Loglama
        LogException(exception, statusCode, requestId, path, method, userId);

        // Response gönder
        context.Response.StatusCode = statusCode;
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await context.Response.WriteAsync(json);
    }

    private (int statusCode, string errorCode, string message) GetErrorDetails(Exception exception)
    {
        return exception switch
        {
            // Özel Craftora exception'ları
            CraftoraException craftoraEx =>
                (craftoraEx.StatusCode, craftoraEx.ErrorCode, craftoraEx.Message),

            DbUpdateException { InnerException: PostgresException postgresException }
                when IsInvalidInputError(postgresException.SqlState) =>
                (400, "INVALID_INPUT", "Gönderilen alanlardan biri izin verilen sınırların dışında."),

            PostgresException postgresException
                when IsInvalidInputError(postgresException.SqlState) =>
                (400, "INVALID_INPUT", "Gönderilen alanlardan biri izin verilen sınırların dışında."),

            // İptal edilen istek - loglama yapılmayacak
            OperationCanceledException =>
                (499, "CLIENT_CLOSED_REQUEST", "İstek iptal edildi"),

            // Varsayılan - 500 Internal Server Error
            _ => (500, "INTERNAL_SERVER_ERROR", "Sunucu hatası oluştu")
        };
    }

    private static bool IsInvalidInputError(string sqlState)
    {
        return sqlState is "22001" or "22003" or "23514";
    }

    private string GetSafeMessage(string message, Exception exception, int statusCode)
    {
        // Production'da 5xx hatalar için generic mesaj döndür
        if (!_environment.IsDevelopment() && statusCode >= 500)
        {
            return "Sunucu tarafında bir hata oluştu. Lütfen daha sonra tekrar deneyin.";
        }

        // Aksi halde gerçek mesajı döndür
        return message;
    }

    private void HandleSpecialHeaders(HttpContext context, Exception exception, int statusCode)
    {
        // RateLimitException → Retry-After header
        if (exception is RateLimitException rateLimitEx)
        {
            context.Response.Headers["Retry-After"] = rateLimitEx.RetryAfter.ToString();
        }

        // UnauthorizedException → WWW-Authenticate header
        if (exception is UnauthorizedException)
        {
            context.Response.Headers["WWW-Authenticate"] = "Bearer realm=\"CraftoraApi\"";
        }
    }

    private void LogException(
        Exception exception,
        int statusCode,
        string requestId,
        string path,
        string method,
        string userId)
    {
        // 499 - Client Closed Request: Loglama yapma
        if (statusCode == 499)
        {
            return;
        }

        // Log seviyesi belirleme
        var logLevel = statusCode >= 500
            ? Serilog.Events.LogEventLevel.Error
            : Serilog.Events.LogEventLevel.Warning;

        // Exception'ın tipi
        var exceptionType = exception.GetType().Name;

        // Log mesajı
        if (statusCode >= 500)
        {
            _logger.LogError(
                exception,
                "❌ {ExceptionType} - HTTP {StatusCode} | RequestId: {RequestId} | Path: {Path} {Method} | UserId: {UserId}",
                exceptionType,
                statusCode,
                requestId,
                path,
                method,
                userId);
        }
        else
        {
            _logger.LogWarning(
                "⚠️ {ExceptionType} - HTTP {StatusCode} | RequestId: {RequestId} | Path: {Path} {Method} | UserId: {UserId} | Message: {Message}",
                exceptionType,
                statusCode,
                requestId,
                path,
                method,
                userId,
                exception.Message);
        }
    }
}

/// <summary>
/// ExceptionMiddleware'ı middleware pipeline'ına eklemek için extension method
/// Program.cs'de şöyle kullanılır:
/// app.UseExceptionMiddleware();
/// </summary>
public static class ExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionMiddleware(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ExceptionMiddleware>();
    }
}
