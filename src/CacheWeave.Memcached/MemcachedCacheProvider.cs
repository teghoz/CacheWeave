using CacheWeave.Core.Abstractions;
using Enyim.Caching;

namespace CacheWeave.Memcached;

/// <summary>
/// CacheWeave provider backed by Memcached via EnyimMemcachedCore.
/// </summary>
public sealed class MemcachedCacheProvider : ICacheProviderInner
{
    private readonly IMemcachedClient _client;

    public MemcachedCacheProvider(IMemcachedClient client)
    {
        _client = client;
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var result = await _client.GetValueAsync<string>(key);
        return result;
    }

    public async Task SetAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        // EnyimMemcachedCore SetAsync accepts expiry in seconds (int); 0 = no expiry
        var expirySeconds = expiry.HasValue ? (int)expiry.Value.TotalSeconds : 0;
        await _client.SetAsync(key, value, expirySeconds);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await _client.RemoveAsync(key);
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        // Memcached has no key enumeration or prefix scanning capability by design.
        // Common workaround: namespace versioning — increment a version key for the prefix
        // and incorporate it into all keys under that prefix.
        throw new NotSupportedException(
            "Memcached does not support prefix-based key scanning. " +
            "Use namespace versioning: store a version counter per prefix and embed it in cache keys.");
    }
}
