using System.Diagnostics;
using CacheWeave.Core.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;


namespace CacheWeave.Redis;

/// <summary>
/// CacheWeave provider backed by Redis via <see cref="IConnectionMultiplexer"/>.
/// Supports full prefix-based invalidation via SCAN + DEL.
/// </summary>
public sealed class RedisCacheProvider : ICacheProviderInner
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly RedisCacheOptions _options;

    /// <summary>Uses the default <see cref="RedisCacheOptions"/>.</summary>
    public RedisCacheProvider(IConnectionMultiplexer multiplexer)
        : this(multiplexer, Options.Create(new RedisCacheOptions()))
    {
    }

    public RedisCacheProvider(IConnectionMultiplexer multiplexer, IOptions<RedisCacheOptions> options)
    {
        _multiplexer = multiplexer;
        _options = options.Value;

        if (_options.EvictionRetryWindow < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options),
                $"{nameof(RedisCacheOptions.EvictionRetryWindow)} cannot be negative.");
        if (_options.ReconnectPollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options),
                $"{nameof(RedisCacheOptions.ReconnectPollInterval)} must be greater than zero.");
        if (_options.ScanPageSize < 1)
            throw new ArgumentOutOfRangeException(nameof(options),
                $"{nameof(RedisCacheOptions.ScanPageSize)} must be at least 1.");
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
        var batchSize = _options.ScanPageSize;
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
    /// <see cref="RedisCacheOptions.EvictionRetryWindow"/>. Deleting an already-deleted key is a
    /// no-op, so replaying a partially applied eviction is safe.
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
            if (_options.EvictionRetryWindow <= TimeSpan.Zero || !await WaitForReconnectAsync(cancellationToken))
                throw;
        }

        await evict();
    }

    private async Task<bool> WaitForReconnectAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        while (!_multiplexer.IsConnected)
        {
            if (stopwatch.Elapsed >= _options.EvictionRetryWindow || cancellationToken.IsCancellationRequested)
                return false;

            await Task.Delay(_options.ReconnectPollInterval, cancellationToken);
        }

        return true;
    }
}
