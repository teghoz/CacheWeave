using CacheWeave.Core;
using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Extensions;
using CacheWeave.Core.Serialization;
using CacheWeave.Core.Services;
using CacheWeave.InMemory.Extensions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CacheWeave.Tests.Providers;

/// <summary>
/// Tests for the <c>CacheWeaveOptions.Enabled = false</c> runtime toggle (Fix 5).
/// When disabled, <c>ICacheProvider</c> resolves to <c>DisabledCacheProvider</c>
/// and all operations are no-ops.
/// </summary>
public class DisabledCacheProviderTests
{
    // -------------------------------------------------------------------------
    // ICacheProvider resolution via DI
    // -------------------------------------------------------------------------

    private static ICacheProvider BuildProvider(bool enabled)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCacheWeave(o => o.Enabled = enabled);
        services.AddCacheWeaveInMemory();
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<ICacheProvider>();
    }

    [Fact]
    public void WhenEnabled_ReturnsRealProvider()
    {
        var provider = BuildProvider(enabled: true);
        // Should NOT be the disabled no-op — it should be the InMemory provider
        provider.GetType().Name.Should().NotBe("DisabledCacheProvider");
    }

    [Fact]
    public async Task WhenDisabled_GetAsync_AlwaysReturnsNull()
    {
        var provider = BuildProvider(enabled: false);
        var result = await provider.GetAsync("any-key");
        result.Should().BeNull();
    }

    [Fact]
    public async Task WhenDisabled_SetAsync_IsNoOp()
    {
        var provider = BuildProvider(enabled: false);
        var act = async () => await provider.SetAsync("k", "v", TimeSpan.FromMinutes(1));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task WhenDisabled_RemoveAsync_IsNoOp()
    {
        var provider = BuildProvider(enabled: false);
        var act = async () => await provider.RemoveAsync("k");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task WhenDisabled_RemoveByPrefixAsync_IsNoOp()
    {
        var provider = BuildProvider(enabled: false);
        var act = async () => await provider.RemoveByPrefixAsync("prefix:");
        await act.Should().NotThrowAsync();
    }

    // -------------------------------------------------------------------------
    // End-to-end: CacheWeaveService with Enabled = false
    // -------------------------------------------------------------------------

    private static CacheWeaveService BuildService(bool enabled)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCacheWeave(o => o.Enabled = enabled);
        services.AddCacheWeaveInMemory();
        var sp = services.BuildServiceProvider();
        return (CacheWeaveService)sp.GetRequiredService<ICacheWeaveService>();
    }

    [Fact]
    public async Task WhenDisabled_GetOrSetAsync_AlwaysInvokesFactory()
    {
        var svc = BuildService(enabled: false);
        var callCount = 0;

        // Call twice — factory should be called both times (no caching)
        await svc.GetOrSetAsync<string>("k", _ => { callCount++; return Task.FromResult<string?>("v"); });
        await svc.GetOrSetAsync<string>("k", _ => { callCount++; return Task.FromResult<string?>("v"); });

        callCount.Should().Be(2);
    }

    [Fact]
    public async Task WhenDisabled_SetAsync_DoesNotPersistValue()
    {
        var svc = BuildService(enabled: false);

        await svc.SetAsync("k", "stored-value");
        var result = await svc.GetAsync<string>("k");

        result.Should().BeNull();
    }

    [Fact]
    public async Task WhenEnabled_GetOrSetAsync_CachesAfterFirstCall()
    {
        var svc = BuildService(enabled: true);
        var callCount = 0;

        await svc.GetOrSetAsync<string>("k", _ => { callCount++; return Task.FromResult<string?>("v"); });
        await svc.GetOrSetAsync<string>("k", _ => { callCount++; return Task.FromResult<string?>("v"); });

        // Factory should only be called once — second call hits cache
        callCount.Should().Be(1);
    }
}
