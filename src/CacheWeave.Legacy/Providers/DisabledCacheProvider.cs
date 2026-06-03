using System;
using System.Threading;
using System.Threading.Tasks;
using CacheWeave.Legacy.Abstractions;

namespace CacheWeave.Legacy.Providers
{
    /// <summary>
    /// No-op provider used when caching is globally disabled via <see cref="CacheWeaveOptions.Enabled"/>.
    /// </summary>
    public sealed class DisabledCacheProvider : ICacheProvider
    {
        public static readonly DisabledCacheProvider Instance = new DisabledCacheProvider();

        private DisabledCacheProvider() { }

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task SetAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
