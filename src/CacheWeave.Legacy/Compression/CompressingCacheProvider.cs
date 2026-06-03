using System;
using System.Threading;
using System.Threading.Tasks;
using CacheWeave.Legacy.Abstractions;

namespace CacheWeave.Legacy.Compression
{
    /// <summary>
    /// Decorator that wraps an <see cref="ICacheProviderInner"/> with GZip compression.
    /// </summary>
    public sealed class CompressingCacheProvider : ICacheProvider
    {
        private readonly ICacheProviderInner _inner;
        private readonly ICacheCompressor _compressor;

        public CompressingCacheProvider(ICacheProviderInner inner, ICacheCompressor compressor)
        {
            _inner = inner;
            _compressor = compressor;
        }

        public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            var value = await _inner.GetAsync(key, cancellationToken).ConfigureAwait(false);
            return value is null ? null : _compressor.Decompress(value);
        }

        public Task SetAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        {
            var compressed = _compressor.Compress(value);
            return _inner.SetAsync(key, compressed, expiry, cancellationToken);
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            _inner.RemoveAsync(key, cancellationToken);

        public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
            _inner.RemoveByPrefixAsync(prefix, cancellationToken);
    }
}
