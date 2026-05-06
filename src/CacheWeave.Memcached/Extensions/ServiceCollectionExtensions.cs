using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Extensions;
using Enyim.Caching;
using Enyim.Caching.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheWeave.Memcached.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers CacheWeave with a Memcached backing store.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional EnyimMemcached configuration action.</param>
    public static IServiceCollection AddCacheWeaveMemcached(
        this IServiceCollection services,
        Action<MemcachedClientOptions>? configure = null)
    {
        services.AddCacheWeave();

        if (configure is not null)
            services.AddEnyimMemcached(configure);
        else
            services.AddEnyimMemcached();

        services.AddSingleton<ICacheProviderInner, MemcachedCacheProvider>();
        return services;
    }
}
