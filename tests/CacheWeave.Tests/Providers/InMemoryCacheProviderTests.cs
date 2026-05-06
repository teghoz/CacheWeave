using CacheWeave.InMemory;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace CacheWeave.Tests.Providers;

public class InMemoryCacheProviderTests
{
    private static InMemoryCacheProvider MakeSut()
        => new(new MemoryCache(Options.Create(new MemoryCacheOptions())));

    // -------------------------------------------------------------------------
    // Get / Set
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenKeyNotPresent()
    {
        var sut = MakeSut();
        var result = await sut.GetAsync("missing");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_ReturnsValue()
    {
        var sut = MakeSut();
        await sut.SetAsync("k", "v");
        var result = await sut.GetAsync("k");
        result.Should().Be("v");
    }

    [Fact]
    public async Task SetAsync_WithExpiry_StoresValue()
    {
        var sut = MakeSut();
        await sut.SetAsync("k", "v", TimeSpan.FromMinutes(5));
        var result = await sut.GetAsync("k");
        result.Should().Be("v");
    }

    [Fact]
    public async Task SetAsync_WithNullExpiry_StoresValue()
    {
        var sut = MakeSut();
        await sut.SetAsync("k", "v", expiry: null);
        var result = await sut.GetAsync("k");
        result.Should().Be("v");
    }

    [Fact]
    public async Task SetAsync_OverwritesExistingValue()
    {
        var sut = MakeSut();
        await sut.SetAsync("k", "first");
        await sut.SetAsync("k", "second");
        var result = await sut.GetAsync("k");
        result.Should().Be("second");
    }

    // -------------------------------------------------------------------------
    // Remove
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RemoveAsync_RemovesExistingKey()
    {
        var sut = MakeSut();
        await sut.SetAsync("k", "v");
        await sut.RemoveAsync("k");
        var result = await sut.GetAsync("k");
        result.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_DoesNotThrow_WhenKeyMissing()
    {
        var sut = MakeSut();
        var act = async () => await sut.RemoveAsync("nonexistent");
        await act.Should().NotThrowAsync();
    }

    // -------------------------------------------------------------------------
    // RemoveByPrefix — not supported
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RemoveByPrefixAsync_ThrowsNotSupportedException()
    {
        var sut = MakeSut();
        var act = async () => await sut.RemoveByPrefixAsync("prefix:");
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task RemoveByPrefixAsync_ExceptionMessage_MentionsIMemoryCache()
    {
        var sut = MakeSut();
        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => sut.RemoveByPrefixAsync("prefix:"));
        ex.Message.Should().Contain("IMemoryCache");
    }

    // -------------------------------------------------------------------------
    // CancellationToken is accepted (no-op for in-memory)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_AcceptsCancellationToken()
    {
        var sut = MakeSut();
        using var cts = new CancellationTokenSource();
        var result = await sut.GetAsync("k", cts.Token);
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_AcceptsCancellationToken()
    {
        var sut = MakeSut();
        using var cts = new CancellationTokenSource();
        var act = async () => await sut.SetAsync("k", "v", null, cts.Token);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveAsync_AcceptsCancellationToken()
    {
        var sut = MakeSut();
        using var cts = new CancellationTokenSource();
        var act = async () => await sut.RemoveAsync("k", cts.Token);
        await act.Should().NotThrowAsync();
    }
}
