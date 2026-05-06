using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Serialization;
using CacheWeave.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using CacheWeave.Core;

namespace CacheWeave.Tests.Services;

public class CacheWeaveServiceTests
{
    private readonly Mock<ICacheProvider> _provider = new();
    private readonly SystemTextJsonCacheSerializer _serializer = new();
    private readonly InProcessStampedeProtector _stampede = new();
    private readonly CacheWeaveOptions _opts = new() { DefaultExpiry = TimeSpan.FromMinutes(5) };

    private CacheWeaveService MakeSut() => new(
        _provider.Object,
        _serializer,
        _stampede,
        Options.Create(_opts),
        NullLogger<CacheWeaveService>.Instance);

    private record Item(int Id, string Name);

    // -------------------------------------------------------------------------
    // GetOrSetAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetOrSetAsync_ReturnsCachedValue_OnHit()
    {
        var item = new Item(1, "Steel");
        _provider.Setup(p => p.GetAsync("k", default))
            .ReturnsAsync(_serializer.Serialize(item));

        var sut = MakeSut();
        var result = await sut.GetOrSetAsync<Item>("k", _ => Task.FromResult<Item?>(new Item(99, "Other")));

        result.Should().BeEquivalentTo(item);
        _provider.Verify(p => p.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), default), Times.Never);
    }

    [Fact]
    public async Task GetOrSetAsync_InvokesFactory_OnMiss_AndStores()
    {
        _provider.Setup(p => p.GetAsync("k", default)).ReturnsAsync((string?)null);

        var sut = MakeSut();
        var result = await sut.GetOrSetAsync<Item>("k", _ => Task.FromResult<Item?>(new Item(1, "Steel")));

        result.Should().BeEquivalentTo(new Item(1, "Steel"));
        _provider.Verify(p => p.SetAsync("k", It.IsAny<string>(), _opts.DefaultExpiry, default), Times.Once);
    }

    [Fact]
    public async Task GetOrSetAsync_UsesExplicitExpiry_WhenProvided()
    {
        _provider.Setup(p => p.GetAsync("k", default)).ReturnsAsync((string?)null);
        var sut = MakeSut();

        await sut.GetOrSetAsync<Item>("k", _ => Task.FromResult<Item?>(new Item(1, "X")), TimeSpan.FromSeconds(30));

        _provider.Verify(p => p.SetAsync("k", It.IsAny<string>(), TimeSpan.FromSeconds(30), default), Times.Once);
    }

    [Fact]
    public async Task GetOrSetAsync_DoesNotStore_WhenFactoryReturnsNull()
    {
        _provider.Setup(p => p.GetAsync("k", default)).ReturnsAsync((string?)null);
        var sut = MakeSut();

        var result = await sut.GetOrSetAsync<Item?>("k", _ => Task.FromResult<Item?>(null));

        result.Should().BeNull();
        _provider.Verify(p => p.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), default), Times.Never);
    }

    // -------------------------------------------------------------------------
    // GetAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_ReturnsDeserializedValue_OnHit()
    {
        var item = new Item(5, "Copper");
        _provider.Setup(p => p.GetAsync("k", default)).ReturnsAsync(_serializer.Serialize(item));
        var sut = MakeSut();

        var result = await sut.GetAsync<Item>("k");

        result.Should().BeEquivalentTo(item);
    }

    [Fact]
    public async Task GetAsync_ReturnsDefault_OnMiss()
    {
        _provider.Setup(p => p.GetAsync("k", default)).ReturnsAsync((string?)null);
        var sut = MakeSut();

        var result = await sut.GetAsync<Item>("k");

        result.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // SetAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetAsync_SerializesAndStores()
    {
        var sut = MakeSut();
        var item = new Item(3, "Iron");

        await sut.SetAsync("k", item);

        _provider.Verify(p => p.SetAsync("k", It.IsAny<string>(), _opts.DefaultExpiry, default), Times.Once);
    }

    [Fact]
    public async Task SetAsync_UsesExplicitExpiry()
    {
        var sut = MakeSut();

        await sut.SetAsync("k", new Item(1, "X"), TimeSpan.FromSeconds(10));

        _provider.Verify(p => p.SetAsync("k", It.IsAny<string>(), TimeSpan.FromSeconds(10), default), Times.Once);
    }

    // -------------------------------------------------------------------------
    // InvalidateAsync / InvalidateByPrefixAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvalidateAsync_CallsRemove()
    {
        var sut = MakeSut();
        await sut.InvalidateAsync("k");
        _provider.Verify(p => p.RemoveAsync("k", default), Times.Once);
    }

    [Fact]
    public async Task InvalidateByPrefixAsync_CallsRemoveByPrefix()
    {
        var sut = MakeSut();
        await sut.InvalidateByPrefixAsync("prefix:");
        _provider.Verify(p => p.RemoveByPrefixAsync("prefix:", default), Times.Once);
    }

    // -------------------------------------------------------------------------
    // InvalidateByPrefixesAsync (Fix 4)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvalidateByPrefixesAsync_CallsRemoveByPrefix_ForEachPrefix()
    {
        var sut = MakeSut();
        await sut.InvalidateByPrefixesAsync(["products:", "dashboard:", "stats:"]);

        _provider.Verify(p => p.RemoveByPrefixAsync("products:", default), Times.Once);
        _provider.Verify(p => p.RemoveByPrefixAsync("dashboard:", default), Times.Once);
        _provider.Verify(p => p.RemoveByPrefixAsync("stats:", default), Times.Once);
    }

    [Fact]
    public async Task InvalidateByPrefixesAsync_EmptyList_DoesNotCallProvider()
    {
        var sut = MakeSut();
        await sut.InvalidateByPrefixesAsync([]);
        _provider.Verify(p => p.RemoveByPrefixAsync(It.IsAny<string>(), default), Times.Never);
    }

    // -------------------------------------------------------------------------
    // GetOrSetAsync — nullable factory (Fix 3)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetOrSetAsync_NullableFactory_DoesNotStore_WhenFactoryReturnsNull()
    {
        _provider.Setup(p => p.GetAsync("k", default)).ReturnsAsync((string?)null);
        var sut = MakeSut();

        // Factory explicitly returns null — should skip cache write
        var result = await sut.GetOrSetAsync<Item>("k", _ => Task.FromResult<Item?>(null));

        result.Should().BeNull();
        _provider.Verify(p => p.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), default), Times.Never);
    }
}
