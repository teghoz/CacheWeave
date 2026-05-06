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

    public async Task<T?> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        // Fast path — check cache before acquiring any lock
        var cached = await _provider.GetAsync(key, cancellationToken);
        if (cached is not null)
        {
            _logger.LogDebug("CacheWeave: cache hit for '{Key}'", key);
            return _serializer.Deserialize<T>(cached);
        }

        // Slow path — acquire per-key lock to prevent stampede
        return await _stampedeProtector.ExecuteAsync<T?>(key, async ct =>
        {
            // Re-check inside the lock — another waiter may have already populated it
            var recheck = await _provider.GetAsync(key, ct);
            if (recheck is not null)
            {
                _logger.LogDebug("CacheWeave: cache hit (post-lock recheck) for '{Key}'", key);
                return _serializer.Deserialize<T>(recheck);
            }

            _logger.LogDebug("CacheWeave: cache miss for '{Key}', invoking factory", key);
            var result = await factory(ct);

            if (result is not null)
            {
                var resolved = expiry ?? _options.DefaultExpiry;
                var serialized = _serializer.Serialize(result);
                await _provider.SetAsync(key, serialized, resolved, ct);
                _logger.LogDebug("CacheWeave: stored '{Key}' (TTL: {Expiry})", key, resolved);
            }

            return result;
        }, cancellationToken);
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var cached = await _provider.GetAsync(key, cancellationToken);
        if (cached is null) return default;

        _logger.LogDebug("CacheWeave: cache hit for '{Key}'", key);
        return _serializer.Deserialize<T>(cached);
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        var serialized = _serializer.Serialize(value!);
        var resolved = expiry ?? _options.DefaultExpiry;
        await _provider.SetAsync(key, serialized, resolved, cancellationToken);
        _logger.LogDebug("CacheWeave: explicitly set '{Key}' (TTL: {Expiry})", key, resolved);
    }

    public Task InvalidateAsync(string key, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("CacheWeave: invalidating '{Key}'", key);
        return _provider.RemoveAsync(key, cancellationToken);
    }

    public Task InvalidateByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("CacheWeave: invalidating by prefix '{Prefix}'", prefix);
        return _provider.RemoveByPrefixAsync(prefix, cancellationToken);
    }

    public async Task InvalidateByPrefixesAsync(
        IEnumerable<string> prefixes,
        CancellationToken cancellationToken = default)
    {
        foreach (var prefix in prefixes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogDebug("CacheWeave: invalidating by prefix '{Prefix}'", prefix);
            await _provider.RemoveByPrefixAsync(prefix, cancellationToken);
        }
    }
}
