using CacheWeave.Core.Abstractions;
using StackExchange.Redis;


namespace CacheWeave.Redis;

/// <summary>
/// CacheWeave provider backed by Redis via <see cref="IConnectionMultiplexer"/>.
/// Supports full prefix-based invalidation via SCAN + DEL.
/// </summary>
public sealed class RedisCacheProvider : ICacheProviderInner
{
    private readonly IConnectionMultiplexer _multiplexer;

    public RedisCacheProvider(IConnectionMultiplexer multiplexer)
    {
        _multiplexer = multiplexer;
    }

    private IDatabase Db => _multiplexer.GetDatabase();

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var value = await Db.StringGetAsync(key);
        return value.HasValue ? value.ToString() : null;
    }

    public async Task SetAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        await Db.StringSetAsync(key, value, expiry);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await Db.KeyDeleteAsync(key);
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        const int batchSize = 250;
        var pattern = $"{prefix}*";
        var db = Db;
        var batch = new List<RedisKey>(batchSize);

        foreach (var server in _multiplexer.GetServers())
        {
            // KeysAsync uses SCAN internally — non-blocking, cursor-based, safe for production
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

            // Flush any remaining keys from the last partial batch
            if (batch.Count > 0)
            {
                await db.KeyDeleteAsync(batch.ToArray());
                batch.Clear();
            }
        }
    }
}
