using CacheWeave.Core.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace CacheWeave.InMemory;

/// <summary>
/// CacheWeave provider backed by <see cref="IMemoryCache"/>. Suitable for single-instance deployments and testing.
/// </summary>
public sealed class InMemoryCacheProvider : ICacheProviderInner
{
    private readonly IMemoryCache _cache;

    public InMemoryCacheProvider(IMemoryCache cache)
    {
        _cache = cache;
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.TryGetValue(key, out string? value);
        return Task.FromResult(value);
    }

    public Task SetAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var options = new MemoryCacheEntryOptions();
        if (expiry.HasValue)
            options.SetAbsoluteExpiration(expiry.Value);

        _cache.Set(key, value, options);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        // IMemoryCache does not expose key enumeration.
        // For prefix invalidation in tests or dev, use a keyed wrapper or MemoryCache directly.
        throw new NotSupportedException(
            "Prefix-based removal is not supported by IMemoryCache. " +
            "Consider tracking keys manually or using a different provider.");
    }
}
