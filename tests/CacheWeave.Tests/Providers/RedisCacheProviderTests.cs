using CacheWeave.Redis;
using FluentAssertions;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace CacheWeave.Tests.Providers;

/// <summary>
/// Tests for <see cref="RedisCacheProvider"/> using a mocked <see cref="IConnectionMultiplexer"/>.
/// No live Redis instance required.
/// </summary>
public class RedisCacheProviderTests
{
    private readonly Mock<IConnectionMultiplexer> _multiplexer = new();
    private readonly Mock<IDatabase> _db = new();
    private readonly Mock<IServer> _server = new();

    private RedisCacheProvider MakeSut()
    {
        _multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(_db.Object);
        _multiplexer.Setup(m => m.GetServers()).Returns([_server.Object]);
        return new RedisCacheProvider(_multiplexer.Object);
    }

    // -------------------------------------------------------------------------
    // GetAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_ReturnsValue_WhenKeyExists()
    {
        _db.Setup(d => d.StringGetAsync("k", It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue("hello"));

        var sut = MakeSut();
        var result = await sut.GetAsync("k");

        result.Should().Be("hello");
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenKeyMissing()
    {
        _db.Setup(d => d.StringGetAsync("k", It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var sut = MakeSut();
        var result = await sut.GetAsync("k");

        result.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // SetAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetAsync_CallsStringSet_WithExpiry()
    {
        _db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<bool>(),
                It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var sut = MakeSut();
        await sut.SetAsync("k", "v", TimeSpan.FromMinutes(5));

        _db.Verify(d => d.StringSetAsync(
            "k", "v", TimeSpan.FromMinutes(5),
            It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task SetAsync_CallsStringSet_WithNullExpiry()
    {
        _db.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<bool>(),
                It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var sut = MakeSut();
        await sut.SetAsync("k", "v", expiry: null);

        _db.Verify(d => d.StringSetAsync(
            "k", "v", null,
            It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    // -------------------------------------------------------------------------
    // RemoveAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RemoveAsync_CallsKeyDelete()
    {
        _db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var sut = MakeSut();
        await sut.RemoveAsync("k");

        _db.Verify(d => d.KeyDeleteAsync((RedisKey)"k", It.IsAny<CommandFlags>()), Times.Once);
    }

    // -------------------------------------------------------------------------
    // RemoveByPrefixAsync — batching (Fix 6)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RemoveByPrefixAsync_DeletesMatchingKeys_InBatches()
    {
        // Simulate 5 keys returned by SCAN
        var keys = Enumerable.Range(1, 5)
            .Select(i => new RedisKey($"products:item:{i}"))
            .ToAsyncEnumerable();

        _server.Setup(s => s.KeysAsync(
                It.IsAny<int>(), It.IsAny<RedisValue>(),
                It.IsAny<int>(), It.IsAny<long>(),
                It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns(keys);

        _db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(5L);

        var sut = MakeSut();
        await sut.RemoveByPrefixAsync("products:");

        // All 5 keys fit in one batch of 250 — one bulk delete call
        _db.Verify(d => d.KeyDeleteAsync(
            It.Is<RedisKey[]>(k => k.Length == 5),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task RemoveByPrefixAsync_NoKeys_DoesNotCallDelete()
    {
        _server.Setup(s => s.KeysAsync(
                It.IsAny<int>(), It.IsAny<RedisValue>(),
                It.IsAny<int>(), It.IsAny<long>(),
                It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns(AsyncEnumerable.Empty<RedisKey>());

        var sut = MakeSut();
        await sut.RemoveByPrefixAsync("products:");

        _db.Verify(d => d.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task RemoveByPrefixAsync_BatchesCorrectly_WhenKeysExceedBatchSize()
    {
        // 260 keys — should produce 2 batches (250 + 10)
        var keys = Enumerable.Range(1, 260)
            .Select(i => new RedisKey($"products:{i}"))
            .ToAsyncEnumerable();

        _server.Setup(s => s.KeysAsync(
                It.IsAny<int>(), It.IsAny<RedisValue>(),
                It.IsAny<int>(), It.IsAny<long>(),
                It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns(keys);

        _db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(0L);

        var sut = MakeSut();
        await sut.RemoveByPrefixAsync("products:");

        // First batch: 250 keys, second batch: 10 keys
        _db.Verify(d => d.KeyDeleteAsync(
            It.Is<RedisKey[]>(k => k.Length == 250),
            It.IsAny<CommandFlags>()), Times.Once);
        _db.Verify(d => d.KeyDeleteAsync(
            It.Is<RedisKey[]>(k => k.Length == 10),
            It.IsAny<CommandFlags>()), Times.Once);
    }
}

// Helper to convert IEnumerable<RedisKey> to IAsyncEnumerable<RedisKey>
file static class AsyncEnumerableExtensions
{
    public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
        => new AsyncEnumerableWrapper<T>(source);

    private sealed class AsyncEnumerableWrapper<T>(IEnumerable<T> source) : IAsyncEnumerable<T>
    {
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken ct = default)
            => new AsyncEnumeratorWrapper<T>(source.GetEnumerator());
    }

    private sealed class AsyncEnumeratorWrapper<T>(IEnumerator<T> inner) : IAsyncEnumerator<T>
    {
        public T Current => inner.Current;
        public ValueTask<bool> MoveNextAsync() => new(inner.MoveNext());
        public ValueTask DisposeAsync() { inner.Dispose(); return ValueTask.CompletedTask; }
    }
}

file static class AsyncEnumerable
{
    public static IAsyncEnumerable<T> Empty<T>() => new EmptyAsyncEnumerable<T>();

    private sealed class EmptyAsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken ct = default)
            => new EmptyAsyncEnumerator<T>();
    }

    private sealed class EmptyAsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        public T Current => default!;
        public ValueTask<bool> MoveNextAsync() => new(false);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
