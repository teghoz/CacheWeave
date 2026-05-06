using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
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
    {
        services.AddCacheWeave();
        services.AddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(redisConnectionString));
        services.AddSingleton<ICacheProviderInner, RedisCacheProvider>();
        return services;
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
