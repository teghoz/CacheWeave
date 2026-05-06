using Alachisoft.NCache.Client;
using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace CacheWeave.NCache.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers CacheWeave with an NCache backing store.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="cacheName">The NCache cache name.</param>
    public static IServiceCollection AddCacheWeaveNCache(
        this IServiceCollection services,
        string cacheName)
    {
        services.AddCacheWeave();
        services.AddSingleton<ICache>(_ => CacheManager.GetCache(cacheName));
        services.AddSingleton<ICacheProviderInner, NCacheCacheProvider>();
        return services;
    }
}
