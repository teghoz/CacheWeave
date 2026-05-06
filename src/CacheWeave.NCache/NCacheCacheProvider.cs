using Alachisoft.NCache.Client;
using Alachisoft.NCache.Runtime.Caching;
using CacheWeave.Core.Abstractions;

namespace CacheWeave.NCache;

/// <summary>
/// CacheWeave provider backed by Alachisoft NCache.
/// </summary>
public sealed class NCacheCacheProvider : ICacheProvider, IDisposable
{
    private readonly ICache _cache;

    public NCacheCacheProvider(ICache cache)
    {
        _cache = cache;
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var item = _cache.Get<string>(key);
        return Task.FromResult<string?>(item);
    }

    public Task SetAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var item = new CacheItem(value);
        if (expiry.HasValue)
            item.Expiration = new Expiration(ExpirationType.Absolute, expiry.Value);

        _cache.Insert(key, item);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        // NCache supports tag-based and group-based invalidation.
        // Prefix scanning via SearchService is available but requires a query license.
        // Recommended: use NCache tags at write time and invalidate by tag instead.
        throw new NotSupportedException(
            "Use tag-based invalidation via NCache SearchService for prefix removal.");
    }

    public void Dispose() => _cache.Dispose();
}
