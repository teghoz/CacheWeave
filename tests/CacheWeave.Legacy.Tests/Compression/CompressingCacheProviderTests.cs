using System;
using System.Threading;
using System.Threading.Tasks;
using CacheWeave.Legacy.Abstractions;
using CacheWeave.Legacy.Compression;
using FluentAssertions;
using Moq;
using Xunit;

namespace CacheWeave.Legacy.Tests.Compression
{
    public class CompressingCacheProviderTests
    {
        private readonly Mock<ICacheProviderInner> _inner = new Mock<ICacheProviderInner>();
        private readonly GZipCacheCompressor _compressor = new GZipCacheCompressor();

        private CompressingCacheProvider MakeSut() =>
            new CompressingCacheProvider(_inner.Object, _compressor);

        [Fact]
        public async Task SetAsync_CompressesValueBeforeStoringInInner()
        {
            string? stored = null;
            _inner.Setup(p => p.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                  .Callback<string, string, TimeSpan?, CancellationToken>((_, v, __, ___) => stored = v)
                  .Returns(Task.CompletedTask);

            var sut = MakeSut();
            await sut.SetAsync("k", "hello");

            stored.Should().NotBe("hello");
            _compressor.Decompress(stored!).Should().Be("hello");
        }

        [Fact]
        public async Task GetAsync_DecompressesValueFromInner()
        {
            var compressed = _compressor.Compress("world");
            _inner.Setup(p => p.GetAsync("k", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(compressed);

            var sut = MakeSut();
            var result = await sut.GetAsync("k");

            result.Should().Be("world");
        }

        [Fact]
        public async Task GetAsync_ReturnsNull_WhenInnerReturnsNull()
        {
            _inner.Setup(p => p.GetAsync("k", It.IsAny<CancellationToken>()))
                  .ReturnsAsync((string?)null);

            var sut = MakeSut();
            var result = await sut.GetAsync("k");

            result.Should().BeNull();
        }

        [Fact]
        public async Task RemoveAsync_DelegatesToInner()
        {
            _inner.Setup(p => p.RemoveAsync("k", It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

            var sut = MakeSut();
            await sut.RemoveAsync("k");

            _inner.Verify(p => p.RemoveAsync("k", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RemoveByPrefixAsync_DelegatesToInner()
        {
            _inner.Setup(p => p.RemoveByPrefixAsync("prefix:", It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

            var sut = MakeSut();
            await sut.RemoveByPrefixAsync("prefix:");

            _inner.Verify(p => p.RemoveByPrefixAsync("prefix:", It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
