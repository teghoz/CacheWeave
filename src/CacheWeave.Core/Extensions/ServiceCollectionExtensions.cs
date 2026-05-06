using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Compression;
using CacheWeave.Core.Filters;
using CacheWeave.Core.KeyBuilders;
using CacheWeave.Core.Providers;
using CacheWeave.Core.Serialization;
using CacheWeave.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CacheWeave.Core.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers CacheWeave core services with optional global configuration.
    /// Call a provider extension (e.g. <c>AddCacheWeaveRedis</c>) after this to register the backing store.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optional action to configure <see cref="CacheWeaveOptions"/>.
    /// Use this to set the serializer, log level, metrics toggle, key conventions, and more.
    /// </param>
    /// <example>
    /// <code>
    /// builder.Services.AddCacheWeave(options =>
    /// {
    ///     options.Serializer          = CacheWeaveSerializerType.NewtonsoftJson;
    ///     options.EnableMetrics       = true;
    ///     options.DiagnosticLogLevel  = LogLevel.Information;
    ///     options.KeyVersion          = "v2";
    ///     options.DefaultExpiry       = TimeSpan.FromMinutes(10);
    ///     options.EnableCompression   = true;
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddCacheWeave(
        this IServiceCollection services,
        Action<CacheWeaveOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);
        else
            services.AddOptions<CacheWeaveOptions>();

        // Serializer — resolved at runtime from CacheWeaveOptions.Serializer.
        // Consumers can still override by registering their own ICacheSerializer
        // *before* calling AddCacheWeave (TryAdd semantics).
        services.TryAddSingleton<ICacheSerializer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<CacheWeaveOptions>>().Value;
            return opts.Serializer switch
            {
                CacheWeaveSerializerType.NewtonsoftJson => new NewtonsoftJsonCacheSerializer(),
                _ => new SystemTextJsonCacheSerializer()
            };
        });

        // Key building
        services.TryAddSingleton<ICacheKeyBuilder, DefaultCacheKeyBuilder>();

        // Stampede protection — override with a distributed lock for multi-instance deployments
        services.TryAddSingleton<ICacheStampedeProtector, InProcessStampedeProtector>();

        // Programmatic service
        services.TryAddScoped<ICacheWeaveService, CacheWeaveService>();

        // Compression — wraps ICacheProviderInner when EnableCompression is true.
        // Disabled toggle — when Enabled = false, resolves to a no-op provider regardless
        // of which backing store is registered.
        services.TryAddSingleton<ICacheCompressor, GZipCacheCompressor>();
        services.AddSingleton<ICacheProvider>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<CacheWeaveOptions>>().Value;

            // Master switch — short-circuit everything when caching is disabled
            if (!opts.Enabled)
                return DisabledCacheProvider.Instance;

            var inner = sp.GetService<ICacheProviderInner>();
            if (inner is null)
            {
                sp.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("CacheWeave")
                    .LogWarning(
                        "CacheWeave: no ICacheProviderInner registered. " +
                        "Call AddCacheWeaveRedis(), AddCacheWeaveInMemory(), or another provider extension. " +
                        "Falling back to no-op provider — all cache operations are disabled.");
                return DisabledCacheProvider.Instance;
            }

            if (!opts.EnableCompression) return inner;
            var compressor = sp.GetRequiredService<ICacheCompressor>();
            return new CompressingCacheProvider(inner, compressor);
        });

        // Filters
        services.AddScoped<CacheWeaveFilter>();
        services.AddScoped<CacheWeaveEvictFilter>();
        services.AddScoped<CacheWeavePageFilter>();
        return services;
    }

    /// <summary>
    /// Registers CacheWeave MVC action filters globally so that
    /// <see cref="CacheWeaveAttribute"/> and <see cref="CacheWeaveEvictAttribute"/>
    /// are honoured without any per-controller wiring.
    /// Call this on the <see cref="IMvcBuilder"/> returned by
    /// <c>AddControllers()</c> / <c>AddMvc()</c> after <see cref="AddCacheWeave"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddCacheWeave(options => { ... });
    /// builder.Services.AddControllers().AddCacheWeaveFilters();
    /// </code>
    /// </example>
    public static IMvcBuilder AddCacheWeaveFilters(this IMvcBuilder mvcBuilder)
    {
        mvcBuilder.AddMvcOptions(options =>
        {
            options.Filters.AddService<CacheWeaveFilter>();
            options.Filters.AddService<CacheWeaveEvictFilter>();
        });
        return mvcBuilder;
    }
}
