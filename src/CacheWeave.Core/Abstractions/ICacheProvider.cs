namespace CacheWeave.Core.Abstractions;

/// <summary>
/// Abstraction over a cache backing store. Implement this interface to create a new CacheWeave provider.
/// </summary>
public interface ICacheProvider
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
}
