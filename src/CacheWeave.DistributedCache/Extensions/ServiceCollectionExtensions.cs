using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace CacheWeave.DistributedCache.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers CacheWeave using the existing <c>IDistributedCache</c> registration in the container.
    /// Also registers an <see cref="IConnectionMultiplexer"/> from <paramref name="redisConnectionString"/>
    /// to enable prefix-based invalidation via Redis SCAN.
    /// </summary>
    /// <remarks>
    /// Use this when your application already calls <c>AddStackExchangeRedisCache(...)</c> and you
    /// want CacheWeave to reuse that registration rather than creating a second Redis connection.
    /// </remarks>
    public static IServiceCollection AddCacheWeaveDistributedCache(
        this IServiceCollection services,
        string redisConnectionString)
    {
        services.AddCacheWeave();
        services.AddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(redisConnectionString));
        services.AddSingleton<ICacheProviderInner, DistributedCacheProvider>();
        return services;
    }

    /// <summary>
    /// Registers CacheWeave using the existing <c>IDistributedCache</c> registration in the container,
    /// with an existing <see cref="IConnectionMultiplexer"/> for prefix-based invalidation.
    /// </summary>
    public static IServiceCollection AddCacheWeaveDistributedCache(
        this IServiceCollection services,
        IConnectionMultiplexer multiplexer)
    {
        services.AddCacheWeave();
        services.AddSingleton(multiplexer);
        services.AddSingleton<ICacheProviderInner, DistributedCacheProvider>();
        return services;
    }

    /// <summary>
    /// Registers CacheWeave using the existing <c>IDistributedCache</c> registration in the container,
    /// without prefix-based invalidation support.
    /// </summary>
    /// <remarks>
    /// Use this when you only need exact-key eviction (no <c>[CacheWeaveEvict(Prefix = ...)]</c>),
    /// or when the backing store is not Redis (e.g. <c>AddDistributedMemoryCache</c> in tests).
    /// </remarks>
    public static IServiceCollection AddCacheWeaveDistributedCache(
        this IServiceCollection services)
    {
        services.AddCacheWeave();
        services.AddSingleton<ICacheProviderInner>(sp =>
            new DistributedCacheProvider(
                sp.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(),
                multiplexer: null));
        return services;
    }
}
