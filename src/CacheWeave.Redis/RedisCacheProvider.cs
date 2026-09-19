using System.Diagnostics;
using CacheWeave.Core.Abstractions;
using StackExchange.Redis;


namespace CacheWeave.Redis;

/// <summary>
/// CacheWeave provider backed by Redis via <see cref="IConnectionMultiplexer"/>.
/// Supports full prefix-based invalidation via SCAN + DEL.
/// </summary>
public sealed class RedisCacheProvider : ICacheProviderInner
{
    /// <summary>
    /// How long an eviction waits for the multiplexer to reconnect before giving up. Reads and writes
    /// fail fast — a dropped one is just a cache miss — but a dropped eviction leaves a stale entry
    /// being served until it expires, so evictions ride out a brief failover instead.
    /// </summary>
    private static readonly TimeSpan DefaultEvictionRetryWindow = TimeSpan.FromSeconds(1.5);

    private static readonly TimeSpan ReconnectPollInterval = TimeSpan.FromMilliseconds(50);

    private readonly IConnectionMultiplexer _multiplexer;
    private readonly TimeSpan _evictionRetryWindow;

    public RedisCacheProvider(IConnectionMultiplexer multiplexer)
        : this(multiplexer, DefaultEvictionRetryWindow)
    {
    }

    internal RedisCacheProvider(IConnectionMultiplexer multiplexer, TimeSpan evictionRetryWindow)
    {
        _multiplexer = multiplexer;
        _evictionRetryWindow = evictionRetryWindow;
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

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => EvictAsync(() => Db.KeyDeleteAsync(key), cancellationToken);

    public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
        => EvictAsync(() => ScanAndDeleteAsync(prefix, cancellationToken), cancellationToken);

    private async Task ScanAndDeleteAsync(string prefix, CancellationToken cancellationToken)
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

    /// <summary>
    /// Runs an eviction, retrying once if the connection was unavailable but comes back within
    /// <see cref="_evictionRetryWindow"/>. Deleting an already-deleted key is a no-op, so replaying
    /// a partially applied eviction is safe.
    /// </summary>
    private async Task EvictAsync(Func<Task> evict, CancellationToken cancellationToken)
    {
        try
        {
            await evict();
            return;
        }
        catch (RedisConnectionException)
        {
            if (!await WaitForReconnectAsync(cancellationToken))
                throw;
        }

        await evict();
    }

    private async Task<bool> WaitForReconnectAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        while (!_multiplexer.IsConnected)
        {
            if (stopwatch.Elapsed >= _evictionRetryWindow || cancellationToken.IsCancellationRequested)
                return false;

            await Task.Delay(ReconnectPollInterval, cancellationToken);
        }

        return true;
    }
}
