using System;
using System.Threading.Tasks;
using CacheWeave.Legacy.Extensions;
using CacheWeave.Legacy.Providers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace CacheWeave.Legacy.Tests.Extensions
{
    public class RedisServiceCollectionExtensionsTests
    {
        // Nothing listens on port 1, so connections are refused immediately. The long command timeouts
        // mean a backlogged command would sit for a full minute before failing.
        private const string UnreachableRedis =
            "127.0.0.1:1,connectTimeout=200,connectRetry=1,syncTimeout=60000,asyncTimeout=60000";

        // -------------------------------------------------------------------------
        // BuildRedisConfigurationOptions — connection defaults
        // -------------------------------------------------------------------------

        [Fact]
        public void BuildRedisConfigurationOptions_DoesNotAbortOnConnectFail()
        {
            var options = ServiceCollectionExtensions.BuildRedisConfigurationOptions("localhost:6379", null);

            options.AbortOnConnectFail.Should().BeFalse();
        }

        [Fact]
        public void BuildRedisConfigurationOptions_UsesFailFastBacklogPolicy()
        {
            var options = ServiceCollectionExtensions.BuildRedisConfigurationOptions("localhost:6379", null);

            options.BacklogPolicy.Should().BeSameAs(BacklogPolicy.FailFast);
        }

        [Fact]
        public void BuildRedisConfigurationOptions_PreservesTimeoutsFromConnectionString()
        {
            var options = ServiceCollectionExtensions.BuildRedisConfigurationOptions(
                "localhost:6379,connectTimeout=250,connectRetry=1,syncTimeout=300,asyncTimeout=400",
                null);

            options.ConnectTimeout.Should().Be(250);
            options.ConnectRetry.Should().Be(1);
            options.SyncTimeout.Should().Be(300);
            options.AsyncTimeout.Should().Be(400);
        }

        [Fact]
        public void BuildRedisConfigurationOptions_ConfigureRunsAfterDefaults_AndCanOverrideThem()
        {
            var options = ServiceCollectionExtensions.BuildRedisConfigurationOptions("localhost:6379", o =>
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

            var services = new ServiceCollection()
                .AddSingleton(existing)
                .AddCacheWeaveRedis("localhost:6379");

            services.Should().ContainSingle(d => d.ServiceType == typeof(IConnectionMultiplexer))
                .Which.ImplementationInstance.Should().BeSameAs(existing);
        }

        // -------------------------------------------------------------------------
        // Behaviour against an unreachable Redis
        // -------------------------------------------------------------------------

        [Fact]
        public async Task BuildRedisConfigurationOptions_UnreachableRedis_CommandsFailFastInsteadOfQueueing()
        {
            using var multiplexer = ConnectionMultiplexer.Connect(
                ServiceCollectionExtensions.BuildRedisConfigurationOptions(UnreachableRedis, null));

            var provider = new RedisCacheProvider(multiplexer);

            // Both policies end in RedisConnectionException; the difference is whether the command waits
            // out asyncTimeout in the backlog first. FailFast rejects it immediately.
            await provider.Invoking(p => p.GetAsync("k"))
                .Should().ThrowWithinAsync<RedisConnectionException>(TimeSpan.FromSeconds(10));
        }
    }
}
