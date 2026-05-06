using CacheWeave.Core.Compression;
using FluentAssertions;
using Xunit;

namespace CacheWeave.Tests.Compression;

public class GZipCacheCompressorTests
{
    private readonly GZipCacheCompressor _sut = new();

    [Fact]
    public void Compress_ProducesBase64String()
    {
        var result = _sut.Compress("hello world");
        result.Should().NotBeNullOrEmpty();
        // Base64 strings only contain these chars
        result.Should().MatchRegex(@"^[A-Za-z0-9+/=]+$");
    }

    [Fact]
    public void Decompress_ReturnsOriginalValue()
    {
        const string original = """{"id":1,"name":"Steel Beam"}""";
        var compressed = _sut.Compress(original);
        var decompressed = _sut.Decompress(compressed);
        decompressed.Should().Be(original);
    }

    [Fact]
    public void RoundTrip_WorksForLargePayload()
    {
        var large = string.Join(",", Enumerable.Range(0, 1000).Select(i => $"item-{i}"));
        var compressed = _sut.Compress(large);
        var decompressed = _sut.Decompress(compressed);
        decompressed.Should().Be(large);
    }

    [Fact]
    public void Compress_ProducesSmallerOutput_ForRepetitiveData()
    {
        var repetitive = new string('a', 10_000);
        var compressed = _sut.Compress(repetitive);
        compressed.Length.Should().BeLessThan(repetitive.Length);
    }
}
