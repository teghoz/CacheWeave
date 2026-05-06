namespace CacheWeave.Core.Abstractions;

/// <summary>
/// Programmatic cache API for use cases where the <see cref="CacheWeaveAttribute"/> annotation
/// cannot be applied — background jobs, hosted services, service-layer caching, dynamic keys,
/// and conditional caching logic.
/// </summary>
public interface ICacheWeaveService
{
    /// <summary>
    /// Returns the cached value for <paramref name="key"/> if present.
    /// If not, invokes <paramref name="factory"/>, stores the result, and returns it.
    /// Protected against cache stampede via a distributed lock — only one caller
    /// will invoke the factory concurrently for the same key.
    /// </summary>
    /// <remarks>
    /// Returning <c>null</c> from <paramref name="factory"/> skips the cache write —
    /// equivalent to <see cref="NoCacheCondition.OnEmpty"/> for programmatic use.
    /// </remarks>
    Task<T?> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, or <c>default</c> if not found.
    /// Does not invoke any factory.
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicitly sets a value in the cache.
    /// </summary>
    Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a single cache entry by exact key.
    /// </summary>
    Task InvalidateAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all cache entries whose keys begin with <paramref name="prefix"/>.
    /// </summary>
    Task InvalidateByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all cache entries matching any of the supplied <paramref name="prefixes"/>.
    /// Equivalent to calling <see cref="InvalidateByPrefixAsync"/> for each prefix in sequence,
    /// but expressed as a single call — useful when a mutation invalidates multiple key namespaces.
    /// </summary>
    Task InvalidateByPrefixesAsync(IEnumerable<string> prefixes, CancellationToken cancellationToken = default);
}
