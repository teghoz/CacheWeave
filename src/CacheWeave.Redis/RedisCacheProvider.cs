using CacheWeave.Core.Abstractions;
using Microsoft.Extensions.Caching.Distributed;

namespace CacheWeave.Redis;

/// <summary>
/// CacheWeave provider backed by Redis via <see cref="IDistributedCache"/>.
/// </summary>
public sealed class RedisCacheProvider : ICacheProvider
{
    private readonly IDistributedCache _cache;

    public RedisCacheProvider(IDistributedCache cache)
    {
        _cache = cache;
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        => _cache.GetStringAsync(key, cancellationToken);

    public Task SetAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var options = new DistributedCacheEntryOptions();
        if (expiry.HasValue)
            options.SetAbsoluteExpiration(expiry.Value);

        return _cache.SetStringAsync(key, value, options, cancellationToken);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => _cache.RemoveAsync(key, cancellationToken);

    public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        // IDistributedCache does not support prefix scanning natively.
        // Full prefix-based invalidation requires direct StackExchange.Redis access.
        // This is a known limitation — override with RedisCacheProviderAdvanced if needed.
        throw new NotSupportedException(
            "Prefix-based removal requires direct IConnectionMultiplexer access. " +
            "Use CacheWeave.Redis.Advanced for this feature.");
    }
}
