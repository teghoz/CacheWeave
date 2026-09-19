using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace CacheWeave.Redis.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers CacheWeave with a Redis backing store via <see cref="IConnectionMultiplexer"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="redisConnectionString">Redis connection string (e.g. "localhost:6379").</param>
    public static IServiceCollection AddCacheWeaveRedis(
        this IServiceCollection services,
        string redisConnectionString)
        => services.AddCacheWeaveRedis(redisConnectionString, configure: null);

    /// <summary>
    /// Registers CacheWeave with a Redis backing store via <see cref="IConnectionMultiplexer"/>,
    /// allowing the parsed <see cref="ConfigurationOptions"/> to be customised.
    /// </summary>
    /// <remarks>
    /// Redis is treated as optional: <see cref="ConfigurationOptions.AbortOnConnectFail"/> is <c>false</c>
    /// and <see cref="ConfigurationOptions.BacklogPolicy"/> is <see cref="BacklogPolicy.FailFast"/>, so an
    /// unreachable Redis degrades to an immediate cache miss instead of blocking every command for
    /// <c>syncTimeout</c>. <paramref name="configure"/> runs after these defaults and can override them.
    /// If an <see cref="IConnectionMultiplexer"/> is already registered, it is used as-is.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="redisConnectionString">Redis connection string (e.g. "localhost:6379").</param>
    /// <param name="configure">Optional callback to adjust the connection options (TLS, auth, timeouts, etc.).</param>
    public static IServiceCollection AddCacheWeaveRedis(
        this IServiceCollection services,
        string redisConnectionString,
        Action<ConfigurationOptions>? configure)
    {
        services.AddCacheWeave();

        if (string.IsNullOrWhiteSpace(redisConnectionString))
            return services;

        services.TryAddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(BuildConfigurationOptions(redisConnectionString, configure)));
        services.AddSingleton<ICacheProviderInner, RedisCacheProvider>();
        return services;
    }

    internal static ConfigurationOptions BuildConfigurationOptions(
        string redisConnectionString,
        Action<ConfigurationOptions>? configure)
    {
        var options = ConfigurationOptions.Parse(redisConnectionString);

        // The cache is optional — CacheWeave already treats any cache failure as a miss. Never throw
        // on connect, and never queue commands waiting for a connection that isn't there.
        options.AbortOnConnectFail = false;
        options.BacklogPolicy = BacklogPolicy.FailFast;

        configure?.Invoke(options);
        return options;
    }

    /// <summary>
    /// Registers CacheWeave with an existing <see cref="IConnectionMultiplexer"/> instance.
    /// Use this overload if your app already registers Redis separately.
    /// </summary>
    public static IServiceCollection AddCacheWeaveRedis(
        this IServiceCollection services,
        IConnectionMultiplexer multiplexer)
    {
        services.AddCacheWeave();
        services.AddSingleton(multiplexer);
        services.AddSingleton<ICacheProviderInner, RedisCacheProvider>();
        return services;
    }
}
