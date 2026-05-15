using CacheWeave.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CacheWeave.Core.Services;

/// <summary>
/// Default implementation of <see cref="ICacheWeaveService"/>.
/// Provides programmatic cache access with stampede protection.
/// </summary>
public sealed class CacheWeaveService : ICacheWeaveService
{
    private readonly ICacheProvider _provider;
    private readonly ICacheSerializer _serializer;
    private readonly ICacheStampedeProtector _stampedeProtector;
    private readonly CacheWeaveOptions _options;
    private readonly ILogger<CacheWeaveService> _logger;

    public CacheWeaveService(
        ICacheProvider provider,
        ICacheSerializer serializer,
        ICacheStampedeProtector stampedeProtector,
        IOptions<CacheWeaveOptions> options,
        ILogger<CacheWeaveService> logger)
    {
        _provider = provider;
        _serializer = serializer;
        _stampedeProtector = stampedeProtector;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Prepends the <see cref="CacheWeaveOptions.GlobalKeyPrefix"/> to the given key
    /// so that programmatic usage is consistent with attribute-based caching.
    /// </summary>
    private string PrefixKey(string key)
    {
        if (string.IsNullOrWhiteSpace(_options.GlobalKeyPrefix))
            return key;

        var sep = _options.KeySeparator ?? ":";
        return key.StartsWith($"{_options.GlobalKeyPrefix}{sep}", StringComparison.Ordinal)
            ? key
            : $"{_options.GlobalKeyPrefix}{sep}{key}";
    }

    public async Task<T?> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        var prefixedKey = PrefixKey(key);

        // Fast path — check cache before acquiring any lock
        var cached = await _provider.GetAsync(prefixedKey, cancellationToken);
        if (cached is not null)
        {
            _logger.LogDebug("CacheWeave: cache hit for '{Key}'", prefixedKey);
            return _serializer.Deserialize<T>(cached);
        }

        // Slow path — acquire per-key lock to prevent stampede
        return await _stampedeProtector.ExecuteAsync<T?>(prefixedKey, async ct =>
        {
            // Re-check inside the lock — another waiter may have already populated it
            var recheck = await _provider.GetAsync(prefixedKey, ct);
            if (recheck is not null)
            {
                _logger.LogDebug("CacheWeave: cache hit (post-lock recheck) for '{Key}'", prefixedKey);
                return _serializer.Deserialize<T>(recheck);
            }

            _logger.LogDebug("CacheWeave: cache miss for '{Key}', invoking factory", prefixedKey);
            var result = await factory(ct);

            if (result is not null)
            {
                var resolved = expiry ?? _options.DefaultExpiry;
                var serialized = _serializer.Serialize(result);
                await _provider.SetAsync(prefixedKey, serialized, resolved, ct);
                _logger.LogDebug("CacheWeave: stored '{Key}' (TTL: {Expiry})", prefixedKey, resolved);
            }

            return result;
        }, cancellationToken);
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var prefixedKey = PrefixKey(key);
        var cached = await _provider.GetAsync(prefixedKey, cancellationToken);
        if (cached is null) return default;

        _logger.LogDebug("CacheWeave: cache hit for '{Key}'", prefixedKey);
        return _serializer.Deserialize<T>(cached);
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        var prefixedKey = PrefixKey(key);
        var serialized = _serializer.Serialize(value!);
        var resolved = expiry ?? _options.DefaultExpiry;
        await _provider.SetAsync(prefixedKey, serialized, resolved, cancellationToken);
        _logger.LogDebug("CacheWeave: explicitly set '{Key}' (TTL: {Expiry})", prefixedKey, resolved);
    }

    public Task InvalidateAsync(string key, CancellationToken cancellationToken = default)
    {
        var prefixedKey = PrefixKey(key);
        _logger.LogDebug("CacheWeave: invalidating '{Key}'", prefixedKey);
        return _provider.RemoveAsync(prefixedKey, cancellationToken);
    }

    public Task InvalidateByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var prefixedPrefix = PrefixKey(prefix);
        _logger.LogDebug("CacheWeave: invalidating by prefix '{Prefix}'", prefixedPrefix);
        return _provider.RemoveByPrefixAsync(prefixedPrefix, cancellationToken);
    }

    public async Task InvalidateByPrefixesAsync(
        IEnumerable<string> prefixes,
        CancellationToken cancellationToken = default)
    {
        foreach (var prefix in prefixes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prefixedPrefix = PrefixKey(prefix);
            _logger.LogDebug("CacheWeave: invalidating by prefix '{Prefix}'", prefixedPrefix);
            await _provider.RemoveByPrefixAsync(prefixedPrefix, cancellationToken);
        }
    }
}
