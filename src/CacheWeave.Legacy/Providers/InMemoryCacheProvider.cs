using System;
using System.Runtime.Caching;
using System.Threading;
using System.Threading.Tasks;
using CacheWeave.Legacy.Abstractions;

namespace CacheWeave.Legacy.Providers
{
    /// <summary>
    /// In-memory cache provider using <see cref="System.Runtime.Caching.MemoryCache"/>,
    /// which is available in-box on .NET Framework 4.8.
    /// Note: prefix-based removal is not supported.
    /// </summary>
    public sealed class InMemoryCacheProvider : ICacheProviderInner, IDisposable
    {
        private readonly MemoryCache _cache;

        public InMemoryCacheProvider() : this(new MemoryCache("CacheWeave")) { }

        public InMemoryCacheProvider(MemoryCache cache)
        {
            _cache = cache;
        }

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            var value = _cache.Get(key) as string;
            return Task.FromResult<string?>(value);
        }

        public Task SetAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        {
            var policy = new CacheItemPolicy();
            if (expiry.HasValue)
                policy.AbsoluteExpiration = DateTimeOffset.UtcNow.Add(expiry.Value);

            _cache.Set(key, value, policy);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _cache.Remove(key);
            return Task.CompletedTask;
        }

        public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "Prefix-based removal is not supported by the InMemory provider on .NET Framework 4.8. " +
                "Use Redis or SQLite if you need prefix eviction.");

        public void Dispose() => _cache.Dispose();
    }
}
