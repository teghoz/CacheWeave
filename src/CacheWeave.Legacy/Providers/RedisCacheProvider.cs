using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CacheWeave.Legacy.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace CacheWeave.Legacy.Providers
{
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
                    nameof(RedisCacheOptions.EvictionRetryWindow) + " cannot be negative.");
            if (_options.ReconnectPollInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options),
                    nameof(RedisCacheOptions.ReconnectPollInterval) + " must be greater than zero.");
            if (_options.ScanPageSize < 1)
                throw new ArgumentOutOfRangeException(nameof(options),
                    nameof(RedisCacheOptions.ScanPageSize) + " must be at least 1.");
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
            var batchSize = _options.ScanPageSize;
            var db = Db;
            foreach (var server in _multiplexer.GetServers())
            {
                if (server.IsReplica)
                    continue;

                var batch = new List<RedisKey>(batchSize);
                await foreach (var key in server.KeysAsync(pattern: $"{prefix}*", pageSize: batchSize).ConfigureAwait(false))
                {
                    batch.Add(key);
                    if (batch.Count == batchSize)
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
        /// <see cref="RedisCacheOptions.EvictionRetryWindow"/>. Deleting an already-deleted key is a
        /// no-op, so replaying a partially applied eviction is safe.
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
                if (_options.EvictionRetryWindow <= TimeSpan.Zero
                    || !await WaitForReconnectAsync(cancellationToken).ConfigureAwait(false))
                    throw;
            }

            await evict().ConfigureAwait(false);
        }

        private async Task<bool> WaitForReconnectAsync(CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();

            while (!_multiplexer.IsConnected)
            {
                if (stopwatch.Elapsed >= _options.EvictionRetryWindow || cancellationToken.IsCancellationRequested)
                    return false;

                await Task.Delay(_options.ReconnectPollInterval, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
    }
}
