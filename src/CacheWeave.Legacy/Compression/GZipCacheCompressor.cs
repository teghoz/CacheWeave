using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using CacheWeave.Legacy.Abstractions;

namespace CacheWeave.Legacy.Compression
{
    public sealed class GZipCacheCompressor : ICacheCompressor
    {
        private const CompressionLevel Level = CompressionLevel.Fastest;

        public string Compress(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, Level, leaveOpen: true))
            {
                gzip.Write(bytes, 0, bytes.Length);
            }
            return Convert.ToBase64String(output.ToArray());
        }

        public string Decompress(string value)
        {
            var bytes = Convert.FromBase64String(value);
            using var input = new MemoryStream(bytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return Encoding.UTF8.GetString(output.ToArray());
        }
    }
}
