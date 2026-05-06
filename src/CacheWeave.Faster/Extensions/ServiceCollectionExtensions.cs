using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace CacheWeave.Faster.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers CacheWeave with a Microsoft FASTER KV backing store.
    /// </summary>
    public static IServiceCollection AddCacheWeaveFaster(
        this IServiceCollection services,
        Action<FasterCacheOptions>? configure = null)
    {
        services.AddCacheWeave();

        if (configure is not null)
            services.Configure(configure);
        else
            services.AddOptions<FasterCacheOptions>();

        services.AddSingleton<ICacheProvider, FasterCacheProvider>();
        return services;
    }
}
