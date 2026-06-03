using System;
using System.Threading;
using System.Threading.Tasks;
using CacheWeave.Legacy.Providers;
using FluentAssertions;
using Xunit;

namespace CacheWeave.Legacy.Tests.Providers
{
    public class InMemoryCacheProviderTests : IDisposable
    {
        private readonly InMemoryCacheProvider _sut = new InMemoryCacheProvider();

        public void Dispose() => _sut.Dispose();

        // -------------------------------------------------------------------------
        // Get / Set
        // -------------------------------------------------------------------------

        [Fact]
        public async Task GetAsync_ReturnsNull_WhenKeyNotPresent()
        {
            var result = await _sut.GetAsync("missing");
            result.Should().BeNull();
        }

        [Fact]
        public async Task SetAsync_ThenGetAsync_ReturnsValue()
        {
            await _sut.SetAsync("k", "v");
            var result = await _sut.GetAsync("k");
            result.Should().Be("v");
        }

        [Fact]
        public async Task SetAsync_WithExpiry_StoresValue()
        {
            await _sut.SetAsync("k", "v", TimeSpan.FromMinutes(5));
            var result = await _sut.GetAsync("k");
            result.Should().Be("v");
        }

        [Fact]
        public async Task SetAsync_WithNullExpiry_StoresValue()
        {
            await _sut.SetAsync("k", "v", expiry: null);
            var result = await _sut.GetAsync("k");
            result.Should().Be("v");
        }

        [Fact]
        public async Task SetAsync_OverwritesExistingValue()
        {
            await _sut.SetAsync("k", "first");
            await _sut.SetAsync("k", "second");
            var result = await _sut.GetAsync("k");
            result.Should().Be("second");
        }

        // -------------------------------------------------------------------------
        // Remove
        // -------------------------------------------------------------------------

        [Fact]
        public async Task RemoveAsync_RemovesExistingKey()
        {
            await _sut.SetAsync("k", "v");
            await _sut.RemoveAsync("k");
            var result = await _sut.GetAsync("k");
            result.Should().BeNull();
        }

        [Fact]
        public async Task RemoveAsync_DoesNotThrow_WhenKeyMissing()
        {
            Func<Task> act = () => _sut.RemoveAsync("nonexistent");
            await act.Should().NotThrowAsync();
        }

        // -------------------------------------------------------------------------
        // RemoveByPrefix — not supported
        // -------------------------------------------------------------------------

        [Fact]
        public async Task RemoveByPrefixAsync_ThrowsNotSupportedException()
        {
            Func<Task> act = () => _sut.RemoveByPrefixAsync("prefix:");
            await act.Should().ThrowAsync<NotSupportedException>();
        }

        [Fact]
        public async Task RemoveByPrefixAsync_ExceptionMessage_MentionsInMemory()
        {
            var ex = await Assert.ThrowsAsync<NotSupportedException>(
                () => _sut.RemoveByPrefixAsync("prefix:"));
            ex.Message.Should().Contain("InMemory");
        }

        // -------------------------------------------------------------------------
        // CancellationToken is accepted
        // -------------------------------------------------------------------------

        [Fact]
        public async Task GetAsync_AcceptsCancellationToken()
        {
            using var cts = new CancellationTokenSource();
            var result = await _sut.GetAsync("k", cts.Token);
            result.Should().BeNull();
        }

        [Fact]
        public async Task SetAsync_AcceptsCancellationToken()
        {
            using var cts = new CancellationTokenSource();
            Func<Task> act = () => _sut.SetAsync("k", "v", null, cts.Token);
            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task RemoveAsync_AcceptsCancellationToken()
        {
            using var cts = new CancellationTokenSource();
            Func<Task> act = () => _sut.RemoveAsync("k", cts.Token);
            await act.Should().NotThrowAsync();
        }
    }
}
