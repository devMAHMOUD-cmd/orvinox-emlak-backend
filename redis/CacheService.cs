using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace CraftoraApi.Redis;

public sealed class CacheService : ICacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string ReleaseLockScript = """
        if redis.call("GET", KEYS[1]) == ARGV[1] then
            return redis.call("DEL", KEYS[1])
        end

        return 0
        """;

    private readonly IDistributedCache _cache;
    private readonly IDatabase _redisDatabase;

    public CacheService(
        IDistributedCache cache,
        IConnectionMultiplexer redis)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _redisDatabase = (redis ?? throw new ArgumentNullException(nameof(redis))).GetDatabase();
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be empty.", nameof(key));
        }

        var cachedValue = await _cache.GetStringAsync(key, cancellationToken);

        return string.IsNullOrWhiteSpace(cachedValue)
            ? default
            : JsonSerializer.Deserialize<T>(cachedValue, JsonOptions);
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? absoluteExpirationRelativeToNow = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be empty.", nameof(key));
        }

        var options = new DistributedCacheEntryOptions();
        if (absoluteExpirationRelativeToNow.HasValue)
        {
            options.AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow;
        }

        var serializedValue = JsonSerializer.Serialize(value, JsonOptions);
        await _cache.SetStringAsync(key, serializedValue, options, cancellationToken);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be empty.", nameof(key));
        }

        await _cache.RemoveAsync(key, cancellationToken);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be empty.", nameof(key));
        }

        var cachedValue = await _cache.GetStringAsync(key, cancellationToken);
        return cachedValue is not null;
    }

    public async Task<bool> TryAcquireLockAsync(string key, string value, TimeSpan expiry)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be empty.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Lock value cannot be empty.", nameof(value));
        }

        if (expiry <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(expiry), "Lock expiry must be positive.");
        }

        return await _redisDatabase.StringSetAsync(
            key,
            value,
            expiry,
            When.NotExists);
    }

    public async Task ReleaseLockAsync(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be empty.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Lock value cannot be empty.", nameof(value));
        }

        await _redisDatabase.ScriptEvaluateAsync(
            ReleaseLockScript,
            [new RedisKey(key)],
            [new RedisValue(value)]);
    }

    public async Task<long> IncrementAsync(
        string key,
        long value = 1,
        TimeSpan? absoluteExpirationRelativeToNow = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be empty.", nameof(key));
        }

        var cachedValue = await _cache.GetStringAsync(key, cancellationToken);
        var currentValue = long.TryParse(cachedValue, out var parsedValue)
            ? parsedValue
            : 0;

        var nextValue = currentValue + value;
        var options = new DistributedCacheEntryOptions();

        if (absoluteExpirationRelativeToNow.HasValue)
        {
            options.AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow;
        }

        await _cache.SetStringAsync(key, nextValue.ToString(), options, cancellationToken);
        return nextValue;
    }

    public async Task AddToSetAsync(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be empty.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Cache set value cannot be empty.", nameof(value));
        }

        await _redisDatabase.SetAddAsync(key, value);
    }

    public async Task<List<string>> GetSetMembersAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be empty.", nameof(key));
        }

        var values = await _redisDatabase.SetMembersAsync(key);

        return values
            .Select(value => value.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    public async Task RemoveFromSetAsync(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Cache key cannot be empty.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Cache set value cannot be empty.", nameof(value));
        }

        await _redisDatabase.SetRemoveAsync(key, value);
    }
}
