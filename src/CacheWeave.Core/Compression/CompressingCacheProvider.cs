using CacheWeave.Core.Abstractions;

namespace CacheWeave.Core.Compression;

/// <summary>
/// Decorator over <see cref="ICacheProvider"/> that transparently compresses values
/// before writing and decompresses after reading.
///
/// Register via <c>AddCacheWeave(o => o.EnableCompression = true)</c> or manually:
/// <code>
/// services.Decorate&lt;ICacheProvider, CompressingCacheProvider&gt;();
/// </code>
/// </summary>
public sealed class CompressingCacheProvider : ICacheProvider
{
    private readonly ICacheProvider _inner;
    private readonly ICacheCompressor _compressor;

    public CompressingCacheProvider(ICacheProvider inner, ICacheCompressor compressor)
    {
        _inner = inner;
        _compressor = compressor;
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var compressed = await _inner.GetAsync(key, cancellationToken);
        return compressed is null ? null : _compressor.Decompress(compressed);
    }

    public Task SetAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var compressed = _compressor.Compress(value);
        return _inner.SetAsync(key, compressed, expiry, cancellationToken);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => _inner.RemoveAsync(key, cancellationToken);

    public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
        => _inner.RemoveByPrefixAsync(prefix, cancellationToken);
}
