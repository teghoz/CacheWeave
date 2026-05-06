using CacheWeave.Core;
using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Extensions;
using CacheWeave.Core.Providers;
using CacheWeave.DistributedCache.Extensions;
using CacheWeave.InMemory.Extensions;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CacheWeave.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    // -------------------------------------------------------------------------
    // AddCacheWeave — core registrations
    // -------------------------------------------------------------------------

    [Fact]
    public void AddCacheWeave_RegistersICacheSerializer()
    {
        var sp = new ServiceCollection()
            .AddLogging()
            .AddCacheWeave()
            .BuildServiceProvider();

        sp.GetService<ICacheSerializer>().Should().NotBeNull();
    }

    [Fact]
    public void AddCacheWeave_RegistersICacheKeyBuilder()
    {
        var sp = new ServiceCollection()
            .AddLogging()
            .AddCacheWeave()
            .BuildServiceProvider();

        sp.GetService<ICacheKeyBuilder>().Should().NotBeNull();
    }

    [Fact]
    public void AddCacheWeave_RegistersICacheStampedeProtector()
    {
        var sp = new ServiceCollection()
            .AddLogging()
            .AddCacheWeave()
            .BuildServiceProvider();

        sp.GetService<ICacheStampedeProtector>().Should().NotBeNull();
    }

    [Fact]
    public void AddCacheWeave_RegistersICacheWeaveService()
    {
        var sp = new ServiceCollection()
            .AddLogging()
            .AddCacheWeave()
            .BuildServiceProvider();

        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetService<ICacheWeaveService>().Should().NotBeNull();
    }

    // -------------------------------------------------------------------------
    // AddCacheWeave — no provider registered → falls back to no-op (all reads null)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AddCacheWeave_FallsBackToNoOp_WhenNoInnerProviderRegistered()
    {
        var sp = new ServiceCollection()
            .AddLogging()
            .AddCacheWeave()
            .BuildServiceProvider();

        var provider = sp.GetRequiredService<ICacheProvider>();
        // No-op provider: reads always return null
        var result = await provider.GetAsync("any-key");
        result.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // AddCacheWeave — Enabled = false → no-op provider (all reads null)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AddCacheWeave_ReturnsNoOpProvider_WhenEnabledIsFalse()
    {
        var sp = new ServiceCollection()
            .AddLogging()
            .AddCacheWeaveInMemory()
            .Configure<CacheWeaveOptions>(o => o.Enabled = false)
            .BuildServiceProvider();

        var provider = sp.GetRequiredService<ICacheProvider>();
        // Disabled provider: reads always return null, writes are no-ops
        var result = await provider.GetAsync("any-key");
        result.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // AddCacheWeave — consumer-registered serializer wins (TryAdd semantics)
    // -------------------------------------------------------------------------

    [Fact]
    public void AddCacheWeave_UsesConsumerSerializer_WhenRegisteredFirst()
    {
        var customSerializer = new CacheWeave.Core.Serialization.NewtonsoftJsonCacheSerializer();

        var sp = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ICacheSerializer>(customSerializer)
            .AddCacheWeave()
            .BuildServiceProvider();

        sp.GetRequiredService<ICacheSerializer>().Should().BeSameAs(customSerializer);
    }

    // -------------------------------------------------------------------------
    // AddCacheWeave — configure overload sets options
    // -------------------------------------------------------------------------

    [Fact]
    public void AddCacheWeave_ConfigureOverload_SetsOptions()
    {
        var sp = new ServiceCollection()
            .AddLogging()
            .AddCacheWeave(o =>
            {
                o.KeyVersion = "v99";
                o.EnableMetrics = true;
            })
            .BuildServiceProvider();

        var opts = sp.GetRequiredService<IOptions<CacheWeaveOptions>>().Value;
        opts.KeyVersion.Should().Be("v99");
        opts.EnableMetrics.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // AddCacheWeaveInMemory
    // -------------------------------------------------------------------------

    [Fact]
    public void AddCacheWeaveInMemory_RegistersInMemoryProvider()
    {
        var sp = new ServiceCollection()
            .AddLogging()
            .AddCacheWeaveInMemory()
            .BuildServiceProvider();

        sp.GetService<ICacheProviderInner>().Should().BeOfType<CacheWeave.InMemory.InMemoryCacheProvider>();
    }

    [Fact]
    public async Task AddCacheWeaveInMemory_ICacheProvider_IsNotDisabled()
    {
        var sp = new ServiceCollection()
            .AddLogging()
            .AddCacheWeaveInMemory()
            .BuildServiceProvider();

        var provider = sp.GetRequiredService<ICacheProvider>();
        // A real provider: write then read back succeeds
        await provider.SetAsync("probe", "value");
        var result = await provider.GetAsync("probe");
        result.Should().Be("value");
    }

    // -------------------------------------------------------------------------
    // AddCacheWeaveDistributedCache — no-multiplexer overload
    // -------------------------------------------------------------------------

    [Fact]
    public void AddCacheWeaveDistributedCache_NoMultiplexer_RegistersProvider()
    {
        var sp = new ServiceCollection()
            .AddLogging()
            .AddDistributedMemoryCache()
            .AddCacheWeaveDistributedCache()
            .BuildServiceProvider();

        sp.GetService<ICacheProviderInner>()
            .Should().BeOfType<CacheWeave.DistributedCache.DistributedCacheProvider>();
    }

    // -------------------------------------------------------------------------
    // AddCacheWeave — EnableCompression = true wraps with CompressingCacheProvider
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AddCacheWeave_WrapsWithCompressingProvider_WhenCompressionEnabled()
    {
        var sp = new ServiceCollection()
            .AddLogging()
            .AddCacheWeaveInMemory()
            .Configure<CacheWeaveOptions>(o => o.EnableCompression = true)
            .BuildServiceProvider();

        var provider = sp.GetRequiredService<ICacheProvider>();
        provider.Should().BeOfType<CacheWeave.Core.Compression.CompressingCacheProvider>();

        // Smoke-test: round-trip through compression works
        await provider.SetAsync("k", "hello");
        var result = await provider.GetAsync("k");
        result.Should().Be("hello");
    }
}
