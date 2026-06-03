using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CacheWeave.Legacy.Abstractions;
using StackExchange.Redis;

namespace CacheWeave.Legacy.Providers
{
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
            var value = await Db.StringGetAsync(key).ConfigureAwait(false);
            return value.HasValue ? value.ToString() : null;
        }

        public Task SetAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) =>
            Db.StringSetAsync(key, value, expiry);

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            Db.KeyDeleteAsync(key);

        public async Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
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
    }
}
