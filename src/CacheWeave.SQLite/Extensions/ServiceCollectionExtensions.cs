using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace CacheWeave.SQLite.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers CacheWeave with a SQLite backing store.
    /// </summary>
    public static IServiceCollection AddCacheWeaveSQLite(
        this IServiceCollection services,
        Action<SQLiteCacheOptions>? configure = null)
    {
        services.AddCacheWeave();

        if (configure is not null)
            services.Configure(configure);
        else
            services.AddOptions<SQLiteCacheOptions>();

        services.AddSingleton<ICacheProviderInner, SQLiteCacheProvider>();
        return services;
    }
}
