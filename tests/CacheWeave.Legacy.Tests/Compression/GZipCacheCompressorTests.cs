using System;
using CacheWeave.Legacy.Compression;
using FluentAssertions;
using Xunit;

namespace CacheWeave.Legacy.Tests.Compression
{
    public class GZipCacheCompressorTests
    {
        private readonly GZipCacheCompressor _sut = new GZipCacheCompressor();

        [Fact]
        public void Compress_ThenDecompress_ReturnsOriginalValue()
        {
            const string original = "Hello, CacheWeave Legacy!";
            var compressed = _sut.Compress(original);
            var decompressed = _sut.Decompress(compressed);
            decompressed.Should().Be(original);
        }

        [Fact]
        public void Compress_ProducesBase64String()
        {
            var compressed = _sut.Compress("test");
            var bytes = Convert.FromBase64String(compressed); // should not throw
            bytes.Should().NotBeEmpty();
        }

        [Fact]
        public void Compress_LargePayload_RoundTrips()
        {
            var large = new string('x', 100_000);
            var compressed = _sut.Compress(large);
            var decompressed = _sut.Decompress(compressed);
            decompressed.Should().Be(large);
        }

        [Fact]
        public void Compress_EmptyString_RoundTrips()
        {
            var compressed = _sut.Compress(string.Empty);
            var decompressed = _sut.Decompress(compressed);
            decompressed.Should().Be(string.Empty);
        }

        [Fact]
        public void Compress_JsonPayload_RoundTrips()
        {
            const string json = "{\"id\":1,\"name\":\"product\",\"price\":9.99}";
            var compressed = _sut.Compress(json);
            var decompressed = _sut.Decompress(compressed);
            decompressed.Should().Be(json);
        }

        [Fact]
        public void Compress_OutputIsSmallerThanInput_ForRepetitiveContent()
        {
            var repetitive = new string('a', 10_000);
            var compressed = _sut.Compress(repetitive);
            compressed.Length.Should().BeLessThan(repetitive.Length);
        }
    }
}
