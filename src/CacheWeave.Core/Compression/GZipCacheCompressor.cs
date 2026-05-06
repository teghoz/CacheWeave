using System.IO.Compression;
using System.Text;
using CacheWeave.Core.Abstractions;

namespace CacheWeave.Core.Compression;

/// <summary>
/// Default <see cref="ICacheCompressor"/> using GZip compression.
/// Stored values are Base64-encoded so they remain string-compatible with all providers.
/// </summary>
public sealed class GZipCacheCompressor : ICacheCompressor
{
    private readonly CompressionLevel _level;

    public GZipCacheCompressor(CompressionLevel level = CompressionLevel.Fastest)
    {
        _level = level;
    }

    public string Compress(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, _level, leaveOpen: true))
            gzip.Write(bytes, 0, bytes.Length);

        return Convert.ToBase64String(output.ToArray());
    }

    public string Decompress(string value)
    {
        var compressed = Convert.FromBase64String(value);
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }
}
