namespace CraftoraApi.Middleware;

/// <summary>
/// Craftora platformu için özel exception sınıfları
/// Tüm custom exception'lar bu base class'tan türer
/// </summary>
public class CraftoraException : Exception
{
    public int StatusCode { get; set; }
    public string ErrorCode { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }

    public CraftoraException(
        string message,
        int statusCode = 500,
        string errorCode = "INTERNAL_SERVER_ERROR",
        Dictionary<string, object>? metadata = null) : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        Metadata = metadata;
    }
}

/// <summary>
/// 404 Not Found
/// Kaynağa dair bir şey bulunamadı
/// </summary>
public class NotFoundException : CraftoraException
{
    public NotFoundException(string resourceName, string resourceId)
        : base(
            $"{resourceName} (ID: {resourceId}) bulunamadı",
            statusCode: 404,
            errorCode: "NOT_FOUND",
            metadata: new() { { "resourceName", resourceName }, { "resourceId", resourceId } })
    {
    }

    public NotFoundException(string message)
        : base(message, statusCode: 404, errorCode: "NOT_FOUND")
    {
    }
}

/// <summary>
/// 401 Unauthorized
/// Kimlik doğrulama gerekli veya başarısız
/// </summary>
public class UnauthorizedException : CraftoraException
{
    public UnauthorizedException(string message = "Kimlik doğrulama gerekli")
        : base(message, statusCode: 401, errorCode: "UNAUTHORIZED")
    {
    }
}

/// <summary>
/// 403 Forbidden
/// Kullanıcı yapma yetkisine sahip değil
/// </summary>
public class ForbiddenException : CraftoraException
{
    public ForbiddenException(string message = "Bu işlemi yapmaya yetkiniz yok")
        : base(message, statusCode: 403, errorCode: "FORBIDDEN")
    {
    }
}

/// <summary>
/// 422 Unprocessable Entity
/// Veriler hatalı - validation hatası
/// </summary>
public class ValidationException : CraftoraException
{
    public List<ValidationError>? Errors { get; set; }

    public ValidationException(string message, List<ValidationError>? errors = null)
        : base(message, statusCode: 422, errorCode: "VALIDATION_ERROR")
    {
        Errors = errors;
    }

    public ValidationException(List<ValidationError> errors)
        : base("Validation hatası", statusCode: 422, errorCode: "VALIDATION_ERROR")
    {
        Errors = errors;
    }
}

/// <summary>
/// Validation hatasının detayı
/// </summary>
public record ValidationError(
    string Field,
    string Message,
    object? AttemptedValue = null);

/// <summary>
/// 409 Conflict
/// İstenen işlem mevcut durumu ile çakışıyor
/// Örnek: Email zaten kayıtlı, duplicate key vs
/// </summary>
public class ConflictException : CraftoraException
{
    public ConflictException(string message)
        : base(message, statusCode: 409, errorCode: "CONFLICT")
    {
    }
}

/// <summary>
/// 429 Too Many Requests
/// Rate limit aşıldı - istek sayısı limiti geçildi
/// </summary>
public class RateLimitException : CraftoraException
{
    public int RetryAfter { get; set; } = 60;

    public RateLimitException(string message = "Rate limit aşıldı. Lütfen daha sonra tekrar deneyin", int retryAfter = 60)
        : base(message, statusCode: 429, errorCode: "RATE_LIMIT_EXCEEDED")
    {
        RetryAfter = retryAfter;
    }
}

/// <summary>
/// 400 Bad Request
/// İstek yanlış formatlandı
/// </summary>
public class BadRequestException : CraftoraException
{
    public BadRequestException(string message)
        : base(message, statusCode: 400, errorCode: "BAD_REQUEST")
    {
    }
}

/// <summary>
/// 502 Bad Gateway
/// Harici servis hata döndürdü (API, DB, Cache vb.)
/// </summary>
public class ExternalServiceException : CraftoraException
{
    public string ServiceName { get; set; }

    public ExternalServiceException(string serviceName, string message)
        : base(
            $"{serviceName} servisi hata döndürdü: {message}",
            statusCode: 502,
            errorCode: "EXTERNAL_SERVICE_ERROR",
            metadata: new() { { "serviceName", serviceName } })
    {
        ServiceName = serviceName;
    }
}

/// <summary>
/// 500 Internal Server Error
/// Veritabanı hatası
/// </summary>
public class DatabaseException : CraftoraException
{
    public DatabaseException(string message, Exception? innerException = null)
        : base(
            $"Veritabanı hatası: {message}",
            statusCode: 500,
            errorCode: "DATABASE_ERROR")
    {
    }
}
