using CraftoraApi.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace CraftoraApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class InfrastructureTestController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IDistributedCache _cache;
    private readonly ILogger<InfrastructureTestController> _logger;

    public InfrastructureTestController(
        AppDbContext dbContext,
        IDistributedCache cache,
        ILogger<InfrastructureTestController> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet("db")]
    public async Task<IActionResult> TestDatabaseAsync(CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(cancellationToken);

        return Ok(new
        {
            status = "ok",
            database = "connected",
            enumMapping = "ok",
            user = user is null
                ? null
                : new
                {
                    user.Id,
                    user.Email,
                    user.FullName,
                    Role = user.Role.ToString()
                }
        });
    }

    [HttpGet("redis")]
    public async Task<IActionResult> TestRedisAsync(CancellationToken cancellationToken)
    {
        const string key = "test_key";
        var value = $"redis-ok-{DateTimeOffset.UtcNow:O}";

        await _cache.SetStringAsync(
            key,
            value,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            },
            cancellationToken);

        var cachedValue = await _cache.GetStringAsync(key, cancellationToken);

        return Ok(new
        {
            status = "ok",
            redis = "connected",
            key,
            value = cachedValue
        });
    }

    [HttpGet("error")]
    public IActionResult TestError()
    {
        throw new Exception("Sistem test hatası!");
    }

    [HttpGet("log")]
    public IActionResult TestLogging()
    {
        _logger.LogInformation("Infrastructure log test: Information seviyesi çalışıyor.");
        _logger.LogWarning("Infrastructure log test: Warning seviyesi çalışıyor.");
        _logger.LogError("Infrastructure log test: Error seviyesi çalışıyor.");

        return Ok(new
        {
            status = "ok",
            logs = new[] { "information", "warning", "error" }
        });
    }
}
