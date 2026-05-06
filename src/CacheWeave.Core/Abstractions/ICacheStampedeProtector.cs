namespace CacheWeave.Core.Abstractions;

/// <summary>
/// Provides mutual exclusion around cache population to prevent cache stampede
/// (thundering herd) — where many concurrent requests all miss the cache and
/// simultaneously invoke the expensive factory.
///
/// The default implementation uses <see cref="SemaphoreSlim"/> (in-process).
/// For multi-instance deployments, replace with a distributed lock implementation
/// (e.g. RedLock via StackExchange.Redis, or a database-backed lock).
/// </summary>
public interface ICacheStampedeProtector
{
    /// <summary>
    /// Acquires a lock for <paramref name="key"/>, invokes <paramref name="factory"/> exactly once,
    /// and releases the lock. Concurrent callers for the same key wait and then re-check the cache
    /// before invoking the factory themselves.
    /// </summary>
    Task<T?> ExecuteAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        CancellationToken cancellationToken = default);
}
