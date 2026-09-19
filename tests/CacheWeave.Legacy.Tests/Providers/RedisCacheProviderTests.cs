using System;
using System.Threading.Tasks;
using CacheWeave.Legacy.Providers;
using FluentAssertions;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace CacheWeave.Legacy.Tests.Providers
{
    /// <summary>
    /// Tests for <see cref="RedisCacheProvider"/> using a mocked <see cref="IConnectionMultiplexer"/>.
    /// No live Redis instance required.
    /// </summary>
    public class RedisCacheProviderTests
    {
        private readonly Mock<IConnectionMultiplexer> _multiplexer = new Mock<IConnectionMultiplexer>();
        private readonly Mock<IDatabase> _db = new Mock<IDatabase>();

        // Zero window when the connection never returns, so the test doesn't wait for it
        private RedisCacheProvider MakeSut(bool reconnects)
        {
            _multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(_db.Object);
            _multiplexer.Setup(m => m.IsConnected).Returns(reconnects);

            return new RedisCacheProvider(
                _multiplexer.Object,
                reconnects ? TimeSpan.FromSeconds(1) : TimeSpan.Zero);
        }

        private static RedisConnectionException Disconnected()
            => new RedisConnectionException(ConnectionFailureType.UnableToConnect, "No connection is active/available");

        // -------------------------------------------------------------------------
        // Eviction retry — a dropped delete leaves a stale entry, so it rides out a blip
        // -------------------------------------------------------------------------

        [Fact]
        public async Task RemoveAsync_RetriesOnce_WhenConnectionReturnsWithinWindow()
        {
            _db.SetupSequence(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(Disconnected())
                .ReturnsAsync(true);

            var sut = MakeSut(reconnects: true);
            await sut.RemoveAsync("k");

            _db.Verify(d => d.KeyDeleteAsync((RedisKey)"k", It.IsAny<CommandFlags>()), Times.Exactly(2));
        }

        [Fact]
        public async Task RemoveAsync_Throws_WhenConnectionDoesNotReturnWithinWindow()
        {
            _db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(Disconnected());

            var sut = MakeSut(reconnects: false);

            await sut.Invoking(s => s.RemoveAsync("k"))
                .Should().ThrowAsync<RedisConnectionException>();
            _db.Verify(d => d.KeyDeleteAsync((RedisKey)"k", It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public async Task GetAsync_DoesNotRetry_WhenConnectionFails()
        {
            _db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(Disconnected());

            var sut = MakeSut(reconnects: true);

            // Reads stay fail-fast — a dropped read is just a cache miss
            await sut.Invoking(s => s.GetAsync("k"))
                .Should().ThrowAsync<RedisConnectionException>();
            _db.Verify(d => d.StringGetAsync((RedisKey)"k", It.IsAny<CommandFlags>()), Times.Once);
        }

        // -------------------------------------------------------------------------
        // Get / Set pass-through
        // -------------------------------------------------------------------------

        [Fact]
        public async Task GetAsync_ReturnsValue_WhenKeyExists()
        {
            _db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(new RedisValue("hello"));

            var result = await MakeSut(reconnects: true).GetAsync("k");

            result.Should().Be("hello");
        }

        [Fact]
        public async Task GetAsync_ReturnsNull_WhenKeyMissing()
        {
            _db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);

            var result = await MakeSut(reconnects: true).GetAsync("k");

            result.Should().BeNull();
        }
    }
}
