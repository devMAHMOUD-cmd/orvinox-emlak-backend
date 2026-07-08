namespace CraftoraApi.Redis;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? absoluteExpirationRelativeToNow = null,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> TryAcquireLockAsync(string key, string value, TimeSpan expiry);

    Task ReleaseLockAsync(string key, string value);

    Task<long> IncrementAsync(
        string key,
        long value = 1,
        TimeSpan? absoluteExpirationRelativeToNow = null,
        CancellationToken cancellationToken = default);

    Task AddToSetAsync(string key, string value);

    Task<List<string>> GetSetMembersAsync(string key);

    Task RemoveFromSetAsync(string key, string value);
}
