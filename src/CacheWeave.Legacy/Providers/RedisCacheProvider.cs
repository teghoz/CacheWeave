using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CacheWeave.Legacy.Abstractions;
using StackExchange.Redis;

namespace CacheWeave.Legacy.Providers
{
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
            var value = await Db.StringGetAsync(key).ConfigureAwait(false);
            return value.HasValue ? value.ToString() : null;
        }

        public Task SetAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) =>
            Db.StringSetAsync(key, value, expiry);

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            EvictAsync(() => Db.KeyDeleteAsync(key), cancellationToken);

        public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
            EvictAsync(() => ScanAndDeleteAsync(prefix), cancellationToken);

        private async Task ScanAndDeleteAsync(string prefix)
        {
            var db = Db;
            foreach (var server in _multiplexer.GetServers())
            {
                if (server.IsReplica)
                    continue;

                var batch = new List<RedisKey>(250);
                await foreach (var key in server.KeysAsync(pattern: $"{prefix}*", pageSize: 250).ConfigureAwait(false))
                {
                    batch.Add(key);
                    if (batch.Count == 250)
                    {
                        await db.KeyDeleteAsync(batch.ToArray()).ConfigureAwait(false);
                        batch.Clear();
                    }
                }

                if (batch.Count > 0)
                    await db.KeyDeleteAsync(batch.ToArray()).ConfigureAwait(false);
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
                await evict().ConfigureAwait(false);
                return;
            }
            catch (RedisConnectionException)
            {
                if (!await WaitForReconnectAsync(cancellationToken).ConfigureAwait(false))
                    throw;
            }

            await evict().ConfigureAwait(false);
        }

        private async Task<bool> WaitForReconnectAsync(CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();

            while (!_multiplexer.IsConnected)
            {
                if (stopwatch.Elapsed >= _evictionRetryWindow || cancellationToken.IsCancellationRequested)
                    return false;

                await Task.Delay(ReconnectPollInterval, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
    }
}
