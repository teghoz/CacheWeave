using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Compression;
using FluentAssertions;
using Moq;
using Xunit;

namespace CacheWeave.Tests.Compression;

public class CompressingCacheProviderTests
{
    private readonly Mock<ICacheProvider> _inner = new();
    private readonly Mock<ICacheCompressor> _compressor = new();
    private readonly CompressingCacheProvider _sut;

    public CompressingCacheProviderTests()
    {
        _sut = new CompressingCacheProvider(_inner.Object, _compressor.Object);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenInnerReturnsNull()
    {
        _inner.Setup(p => p.GetAsync("k", default)).ReturnsAsync((string?)null);

        var result = await _sut.GetAsync("k");

        result.Should().BeNull();
        _compressor.Verify(c => c.Decompress(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetAsync_Decompresses_WhenInnerReturnsValue()
    {
        _inner.Setup(p => p.GetAsync("k", default)).ReturnsAsync("compressed");
        _compressor.Setup(c => c.Decompress("compressed")).Returns("original");

        var result = await _sut.GetAsync("k");

        result.Should().Be("original");
    }

    [Fact]
    public async Task SetAsync_CompressesBeforeStoring()
    {
        _compressor.Setup(c => c.Compress("original")).Returns("compressed");

        await _sut.SetAsync("k", "original", TimeSpan.FromMinutes(1));

        _inner.Verify(p => p.SetAsync("k", "compressed", TimeSpan.FromMinutes(1), default), Times.Once);
    }

    [Fact]
    public async Task RemoveAsync_DelegatesToInner()
    {
        await _sut.RemoveAsync("k");
        _inner.Verify(p => p.RemoveAsync("k", default), Times.Once);
    }

    [Fact]
    public async Task RemoveByPrefixAsync_DelegatesToInner()
    {
        await _sut.RemoveByPrefixAsync("prefix:");
        _inner.Verify(p => p.RemoveByPrefixAsync("prefix:", default), Times.Once);
    }
}
