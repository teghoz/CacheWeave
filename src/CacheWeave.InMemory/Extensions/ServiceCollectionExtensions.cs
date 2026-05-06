using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace CacheWeave.InMemory.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers CacheWeave with an in-memory backing store.
    /// Suitable for development, testing, and single-instance deployments.
    /// </summary>
    public static IServiceCollection AddCacheWeaveInMemory(this IServiceCollection services)
    {
        services.AddCacheWeave();
        services.AddMemoryCache();
        services.AddSingleton<ICacheProviderInner, InMemoryCacheProvider>();
        return services;
    }
}
