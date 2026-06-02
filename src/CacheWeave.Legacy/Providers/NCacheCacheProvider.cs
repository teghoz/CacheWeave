using System;
using System.Threading;
using System.Threading.Tasks;
using Alachisoft.NCache.Client;
using Alachisoft.NCache.Runtime.Caching;
using CacheWeave.Legacy.Abstractions;

namespace CacheWeave.Legacy.Providers
{
    public sealed class NCacheCacheProvider : ICacheProviderInner, IDisposable
    {
        private readonly ICache _cache;

        public NCacheCacheProvider(ICache cache)
        {
            _cache = cache;
        }

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            var value = _cache.Get<string>(key);
            return Task.FromResult<string?>(value);
        }

        public Task SetAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        {
            var item = new CacheItem(value);
            if (expiry.HasValue)
                item.Expiration = new Expiration(ExpirationType.Absolute, expiry.Value);

            _cache.Insert(key, item);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _cache.Remove(key);
            return Task.CompletedTask;
        }

        public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "NCache does not support prefix-based removal in this provider. " +
                "Use Redis or SQLite if you need prefix eviction.");

        public void Dispose() => _cache.Dispose();
    }
}
