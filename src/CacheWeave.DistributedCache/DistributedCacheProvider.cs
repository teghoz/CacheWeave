using CacheWeave.Core.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace CacheWeave.DistributedCache;

/// <summary>
/// CacheWeave provider backed by <see cref="IDistributedCache"/>.
/// <para>
/// This provider allows CacheWeave to reuse an existing <see cref="IDistributedCache"/>
/// registration — for example, one already configured via
/// <c>AddStackExchangeRedisCache</c> or <c>AddDistributedMemoryCache</c>.
/// This is the migration path for applications that already use the Microsoft
/// distributed cache abstraction (e.g. the Cashbox / document-service pattern).
/// </para>
/// <para>
/// Prefix-based invalidation (<see cref="RemoveByPrefixAsync"/>) requires an
/// <see cref="IConnectionMultiplexer"/> to be registered in DI. If no multiplexer
/// is available, prefix removal throws <see cref="InvalidOperationException"/>.
/// </para>
/// </summary>
public sealed class DistributedCacheProvider : ICacheProviderInner
{
    private readonly IDistributedCache _cache;
    private readonly IConnectionMultiplexer? _multiplexer;

    /// <param name="cache">The distributed cache to use for get/set/remove operations.</param>
    /// <param name="multiplexer">
    ///     Optional Redis multiplexer used for SCAN-based prefix invalidation.
    ///     Pass <c>null</c> if prefix invalidation is not required.
    /// </param>
    public DistributedCacheProvider(IDistributedCache cache, IConnectionMultiplexer? multiplexer = null)
    {
        _cache = cache;
        _multiplexer = multiplexer;
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        => await _cache.GetStringAsync(key, cancellationToken);

    /// <inheritdoc />
    public async Task SetAsync(
        string key,
        string value,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        var options = new DistributedCacheEntryOptions();
        if (expiry.HasValue)
            options.AbsoluteExpirationRelativeToNow = expiry;

        await _cache.SetStringAsync(key, value, options, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => await _cache.RemoveAsync(key, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Requires an <see cref="IConnectionMultiplexer"/> to be provided at construction time.
    /// Uses Redis SCAN (non-blocking, cursor-based) to find matching keys, then deletes them
    /// in batches of 250 — matching the pattern used by Cashbox and platform-document-service.
    /// </remarks>
    public async Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        if (_multiplexer is null)
            throw new InvalidOperationException(
                "Prefix-based cache invalidation requires an IConnectionMultiplexer. " +
                "Register one via AddCacheWeaveDistributedCache(connectionString) or " +
                "AddCacheWeaveDistributedCache(multiplexer) and ensure your IDistributedCache " +
                "uses the same Redis instance.");

        const int batchSize = 250;
        var db = _multiplexer.GetDatabase();
        var pattern = $"*{prefix}*";
        var batch = new List<RedisKey>(batchSize);

        foreach (var endpoint in _multiplexer.GetEndPoints())
        {
            var server = _multiplexer.GetServer(endpoint);

            await foreach (var key in server.KeysAsync(pattern: pattern, pageSize: batchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                batch.Add(key);

                if (batch.Count >= batchSize)
                {
                    await db.KeyDeleteAsync(batch.ToArray());
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await db.KeyDeleteAsync(batch.ToArray());
                batch.Clear();
            }
        }
    }
}
