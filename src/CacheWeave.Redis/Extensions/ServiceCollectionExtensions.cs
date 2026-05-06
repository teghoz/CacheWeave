using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace CacheWeave.Redis.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers CacheWeave with a Redis backing store.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="redisConnectionString">Redis connection string (e.g. "localhost:6379").</param>
    public static IServiceCollection AddCacheWeaveRedis(
        this IServiceCollection services,
        string redisConnectionString)
    {
        services.AddCacheWeave();
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnectionString;
        });
        services.AddSingleton<ICacheProvider, RedisCacheProvider>();
        return services;
    }
}
