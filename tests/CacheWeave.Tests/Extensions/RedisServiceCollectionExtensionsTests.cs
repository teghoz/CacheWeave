using CacheWeave.Redis;
using CacheWeave.Redis.Extensions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace CacheWeave.Tests.Extensions;

public class RedisServiceCollectionExtensionsTests
{
    // Nothing listens on port 1, so connections are refused immediately. The long command timeouts
    // mean a backlogged command would sit for a full minute before failing.
    private const string UnreachableRedis =
        "127.0.0.1:1,connectTimeout=200,connectRetry=1,syncTimeout=60000,asyncTimeout=60000";

    // -------------------------------------------------------------------------
    // BuildConfigurationOptions — connection defaults
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildConfigurationOptions_DoesNotAbortOnConnectFail()
    {
        var options = ServiceCollectionExtensions.BuildConfigurationOptions("localhost:6379", configure: null);

        options.AbortOnConnectFail.Should().BeFalse();
    }

    [Fact]
    public void BuildConfigurationOptions_UsesFailFastBacklogPolicy()
    {
        var options = ServiceCollectionExtensions.BuildConfigurationOptions("localhost:6379", configure: null);

        options.BacklogPolicy.Should().BeSameAs(BacklogPolicy.FailFast);
    }

    [Fact]
    public void BuildConfigurationOptions_PreservesTimeoutsFromConnectionString()
    {
        var options = ServiceCollectionExtensions.BuildConfigurationOptions(
            "localhost:6379,connectTimeout=250,connectRetry=1,syncTimeout=300,asyncTimeout=400",
            configure: null);

        options.ConnectTimeout.Should().Be(250);
        options.ConnectRetry.Should().Be(1);
        options.SyncTimeout.Should().Be(300);
        options.AsyncTimeout.Should().Be(400);
    }

    [Fact]
    public void BuildConfigurationOptions_ConfigureRunsAfterDefaults_AndCanOverrideThem()
    {
        var options = ServiceCollectionExtensions.BuildConfigurationOptions("localhost:6379", o =>
        {
            o.BacklogPolicy = BacklogPolicy.Default;
            o.AbortOnConnectFail = true;
        });

        options.BacklogPolicy.Should().BeSameAs(BacklogPolicy.Default);
        options.AbortOnConnectFail.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // AddCacheWeaveRedis(string) — registrations
    // -------------------------------------------------------------------------

    [Fact]
    public void AddCacheWeaveRedis_KeepsMultiplexerRegisteredBeforeIt()
    {
        var existing = new Mock<IConnectionMultiplexer>().Object;

        var sp = new ServiceCollection()
            .AddLogging()
            .AddSingleton(existing)
            .AddCacheWeaveRedis("localhost:6379")
            .BuildServiceProvider();

        sp.GetRequiredService<IConnectionMultiplexer>().Should().BeSameAs(existing);
    }

    [Fact]
    public void AddCacheWeaveRedis_MultiplexerRegisteredAfterIt_StillWins()
    {
        var replacement = new Mock<IConnectionMultiplexer>().Object;

        var sp = new ServiceCollection()
            .AddLogging()
            .AddCacheWeaveRedis("localhost:6379")
            .AddSingleton(replacement)
            .BuildServiceProvider();

        sp.GetRequiredService<IConnectionMultiplexer>().Should().BeSameAs(replacement);
    }

    [Fact]
    public void AddCacheWeaveRedis_BlankConnectionString_RegistersNoMultiplexer()
    {
        var services = new ServiceCollection().AddCacheWeaveRedis(" ");

        services.Should().NotContain(d => d.ServiceType == typeof(IConnectionMultiplexer));
    }

    // -------------------------------------------------------------------------
    // RedisCacheOptions — provider behaviour knobs
    // -------------------------------------------------------------------------

    [Fact]
    public void AddCacheWeaveRedis_DefaultsRedisCacheOptions_WhenNotConfigured()
    {
        var sp = new ServiceCollection()
            .AddLogging()
            .AddCacheWeaveRedis("localhost:6379")
            .BuildServiceProvider();

        var options = sp.GetRequiredService<IOptions<RedisCacheOptions>>().Value;

        options.EvictionRetryWindow.Should().Be(TimeSpan.FromSeconds(1.5));
        options.ReconnectPollInterval.Should().Be(TimeSpan.FromMilliseconds(50));
        options.ScanPageSize.Should().Be(250);
    }

    [Fact]
    public void AddCacheWeaveRedis_AppliesConfigureCache()
    {
        var sp = new ServiceCollection()
            .AddLogging()
            .AddCacheWeaveRedis("localhost:6379", configureConnection: null, configureCache: o =>
            {
                o.EvictionRetryWindow = TimeSpan.Zero;
                o.ScanPageSize = 1000;
            })
            .BuildServiceProvider();

        var options = sp.GetRequiredService<IOptions<RedisCacheOptions>>().Value;

        options.EvictionRetryWindow.Should().Be(TimeSpan.Zero);
        options.ScanPageSize.Should().Be(1000);
    }

    [Fact]
    public void AddCacheWeaveRedis_MultiplexerOverload_AppliesConfigureCache()
    {
        var sp = new ServiceCollection()
            .AddLogging()
            .AddCacheWeaveRedis(new Mock<IConnectionMultiplexer>().Object, o => o.ScanPageSize = 25)
            .BuildServiceProvider();

        sp.GetRequiredService<IOptions<RedisCacheOptions>>().Value.ScanPageSize.Should().Be(25);
    }

    // -------------------------------------------------------------------------
    // Behaviour against an unreachable Redis
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AddCacheWeaveRedis_UnreachableRedis_CommandsFailFastInsteadOfQueueing()
    {
        await using var sp = new ServiceCollection()
            .AddLogging()
            .AddCacheWeaveRedis(UnreachableRedis)
            .BuildServiceProvider();

        var provider = new RedisCacheProvider(sp.GetRequiredService<IConnectionMultiplexer>());

        // Both policies end in RedisConnectionException; the difference is whether the command waits
        // out asyncTimeout in the backlog first. FailFast rejects it immediately.
        await provider.Invoking(p => p.GetAsync("k"))
            .Should().ThrowWithinAsync<RedisConnectionException>(TimeSpan.FromSeconds(10));
    }
}
