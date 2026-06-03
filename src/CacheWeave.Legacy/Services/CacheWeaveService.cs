using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using CacheWeave.Legacy.Abstractions;
using Microsoft.Extensions.Options;

namespace CacheWeave.Legacy.Services
{
    public sealed class CacheWeaveService : ICacheWeaveService
    {
        private readonly ICacheProvider _provider;
        private readonly ICacheSerializer _serializer;
        private readonly ICacheStampedeProtector _stampedeProtector;
        private readonly CacheWeaveOptions _options;

        public CacheWeaveService(
            ICacheProvider provider,
            ICacheSerializer serializer,
            ICacheStampedeProtector stampedeProtector,
            IOptions<CacheWeaveOptions> options)
        {
            _provider = provider;
            _serializer = serializer;
            _stampedeProtector = stampedeProtector;
            _options = options.Value;
        }

        [return: MaybeNull]
        public async Task<T> GetOrSetAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? expiry = null,
            CancellationToken cancellationToken = default)
        {
            var prefixedKey = PrefixKey(key);

            var cached = await _provider.GetAsync(prefixedKey, cancellationToken).ConfigureAwait(false);
            if (cached != null)
#pragma warning disable CS8603
                return _serializer.Deserialize<T>(cached);
#pragma warning restore CS8603

#pragma warning disable CS8602
            return await _stampedeProtector.ExecuteAsync<T>(prefixedKey, async ct =>
            {
#pragma warning restore CS8602
                // Double-checked inside the lock
                var recheck = await _provider.GetAsync(prefixedKey, ct).ConfigureAwait(false);
                if (recheck != null)
#pragma warning disable CS8603
                    return _serializer.Deserialize<T>(recheck);
#pragma warning restore CS8603

                var result = await factory(ct).ConfigureAwait(false);
                if (result != null)
                {
                    var serialized = _serializer.Serialize(result);
                    var resolvedExpiry = expiry ?? _options.DefaultExpiry;
                    await _provider.SetAsync(prefixedKey, serialized, resolvedExpiry, ct).ConfigureAwait(false);
                }
                return result;
            }, cancellationToken).ConfigureAwait(false);
        }

        [return: MaybeNull]
        public async Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            var prefixedKey = PrefixKey(key);
            var cached = await _provider.GetAsync(prefixedKey, cancellationToken).ConfigureAwait(false);
#pragma warning disable CS8603
            return cached is null ? default : _serializer.Deserialize<T>(cached);
#pragma warning restore CS8603
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        {
            var prefixedKey = PrefixKey(key);
            var serialized = _serializer.Serialize(value);
            var resolvedExpiry = expiry ?? _options.DefaultExpiry;
            await _provider.SetAsync(prefixedKey, serialized, resolvedExpiry, cancellationToken).ConfigureAwait(false);
        }

        public Task InvalidateAsync(string key, CancellationToken cancellationToken = default)
        {
            var prefixedKey = PrefixKey(key);
            return _provider.RemoveAsync(prefixedKey, cancellationToken);
        }

        public Task InvalidateByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
        {
            var prefixedPrefix = PrefixKey(prefix);
            return _provider.RemoveByPrefixAsync(prefixedPrefix, cancellationToken);
        }

        public async Task InvalidateByPrefixesAsync(IEnumerable<string> prefixes, CancellationToken cancellationToken = default)
        {
            foreach (var prefix in prefixes)
                await InvalidateByPrefixAsync(prefix, cancellationToken).ConfigureAwait(false);
        }

        private string PrefixKey(string key)
        {
            if (string.IsNullOrEmpty(_options.GlobalKeyPrefix))
                return key;

            var sep = _options.KeySeparator;
            var prefix = _options.GlobalKeyPrefix + sep;
            return key.StartsWith(prefix, StringComparison.Ordinal) ? key : prefix + key;
        }
    }
}
