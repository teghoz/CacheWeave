using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Filters;
using CacheWeave.Core.KeyBuilders;
using Microsoft.Extensions.DependencyInjection;

namespace CacheWeave.Core.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers CacheWeave core services. Call a provider extension (e.g. AddCacheWeaveRedis)
    /// after this to register the backing store.
    /// </summary>
    public static IServiceCollection AddCacheWeave(this IServiceCollection services)
    {
        services.AddSingleton<ICacheKeyBuilder, DefaultCacheKeyBuilder>();
        services.AddScoped<CacheWeaveFilter>();
        return services;
    }
}
