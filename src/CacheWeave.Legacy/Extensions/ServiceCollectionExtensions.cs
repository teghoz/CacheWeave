using System;
using Amazon.DynamoDBv2;
using Alachisoft.NCache.Client;
using CacheWeave.Legacy.Abstractions;
using CacheWeave.Legacy.Compression;
using CacheWeave.Legacy.Providers;
using CacheWeave.Legacy.Serialization;
using CacheWeave.Legacy.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace CacheWeave.Legacy.Extensions
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers CacheWeave core services. Call one of the provider extension methods
        /// (e.g. <see cref="AddCacheWeaveRedis"/>) after this to register a backing store.
        /// </summary>
        public static IServiceCollection AddCacheWeave(
            this IServiceCollection services,
            Action<CacheWeaveOptions>? configure = null)
        {
            var opts = new CacheWeaveOptions();
            configure?.Invoke(opts);

            services.Configure<CacheWeaveOptions>(o =>
            {
                o.Enabled = opts.Enabled;
                o.KeySeparator = opts.KeySeparator;
                o.GlobalKeyPrefix = opts.GlobalKeyPrefix;
                o.KeyVersion = opts.KeyVersion;
                o.DefaultExpiry = opts.DefaultExpiry;
                o.Serializer = opts.Serializer;
                o.EnableCompression = opts.EnableCompression;
            });

            // Serializer
            services.TryAddSingleton<ICacheSerializer>(_ =>
                opts.Serializer == CacheWeaveSerializerType.NewtonsoftJson
                    ? (ICacheSerializer)new NewtonsoftJsonCacheSerializer()
                    : new SystemTextJsonCacheSerializer());

            // Stampede protector
            services.TryAddSingleton<ICacheStampedeProtector, InProcessStampedeProtector>();

            // Compressor
            services.TryAddSingleton<ICacheCompressor, GZipCacheCompressor>();

            // ICacheProvider factory — wraps inner provider with compression if enabled
            services.AddSingleton<ICacheProvider>(sp =>
            {
                if (!opts.Enabled)
                    return DisabledCacheProvider.Instance;

                var inner = sp.GetService<ICacheProviderInner>();
                if (inner is null)
                    return DisabledCacheProvider.Instance;

                if (!opts.EnableCompression)
                    return inner;

                var compressor = sp.GetRequiredService<ICacheCompressor>();
                return new CompressingCacheProvider(inner, compressor);
            });

            // High-level service
            services.TryAddSingleton<ICacheWeaveService, CacheWeaveService>();

            return services;
        }

        // ── Redis ────────────────────────────────────────────────────────────

        /// <summary>Registers the Redis provider using a connection string.</summary>
        public static IServiceCollection AddCacheWeaveRedis(
            this IServiceCollection services,
            string connectionString)
            => services.AddCacheWeaveRedis(connectionString, configureConnection: null);

        /// <summary>
        /// Registers the Redis provider using a connection string, allowing the parsed
        /// <see cref="ConfigurationOptions"/> to be customised.
        /// </summary>
        /// <remarks>
        /// Redis is treated as optional: <see cref="ConfigurationOptions.AbortOnConnectFail"/> is <c>false</c>
        /// and <see cref="ConfigurationOptions.BacklogPolicy"/> is <see cref="BacklogPolicy.FailFast"/>, so an
        /// unreachable Redis degrades to an immediate cache miss instead of blocking every command for
        /// <c>syncTimeout</c>. <paramref name="configureConnection"/> runs after these defaults and can override them.
        /// </remarks>
        /// <param name="configureConnection">Adjusts the connection options (TLS, auth, timeouts, backlog policy).</param>
        /// <param name="configureCache">Adjusts provider behaviour (eviction retry, SCAN page size).</param>
        public static IServiceCollection AddCacheWeaveRedis(
            this IServiceCollection services,
            string connectionString,
            Action<ConfigurationOptions>? configureConnection,
            Action<RedisCacheOptions>? configureCache = null)
        {
            var configOpts = BuildRedisConfigurationOptions(connectionString, configureConnection);
            services.ConfigureRedisCache(configureCache);
            services.TryAddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(configOpts));
            services.TryAddSingleton<ICacheProviderInner, RedisCacheProvider>();
            return services;
        }

        internal static ConfigurationOptions BuildRedisConfigurationOptions(
            string connectionString,
            Action<ConfigurationOptions>? configure)
        {
            var configOpts = ConfigurationOptions.Parse(connectionString);

            // The cache is optional — CacheWeave already treats any cache failure as a miss. Never throw
            // on connect, and never queue commands waiting for a connection that isn't there.
            configOpts.AbortOnConnectFail = false;
            configOpts.BacklogPolicy = BacklogPolicy.FailFast;

            configure?.Invoke(configOpts);
            return configOpts;
        }

        /// <summary>Registers the Redis provider using an existing <see cref="IConnectionMultiplexer"/>.</summary>
        public static IServiceCollection AddCacheWeaveRedis(
            this IServiceCollection services,
            IConnectionMultiplexer multiplexer)
            => services.AddCacheWeaveRedis(multiplexer, configureCache: null);

        /// <summary>
        /// Registers the Redis provider using an existing <see cref="IConnectionMultiplexer"/>,
        /// allowing the provider's own behaviour to be customised.
        /// </summary>
        /// <param name="configureCache">Adjusts provider behaviour (eviction retry, SCAN page size).</param>
        public static IServiceCollection AddCacheWeaveRedis(
            this IServiceCollection services,
            IConnectionMultiplexer multiplexer,
            Action<RedisCacheOptions>? configureCache)
        {
            services.ConfigureRedisCache(configureCache);
            services.TryAddSingleton(multiplexer);
            services.TryAddSingleton<ICacheProviderInner, RedisCacheProvider>();
            return services;
        }

        private static void ConfigureRedisCache(
            this IServiceCollection services,
            Action<RedisCacheOptions>? configureCache)
        {
            if (configureCache != null)
                services.Configure(configureCache);
            else
                services.AddOptions<RedisCacheOptions>();
        }

        // ── InMemory ─────────────────────────────────────────────────────────

        /// <summary>
        /// Registers the in-memory provider backed by <see cref="System.Runtime.Caching.MemoryCache"/>.
        /// Note: prefix-based eviction is not supported.
        /// </summary>
        public static IServiceCollection AddCacheWeaveInMemory(this IServiceCollection services)
        {
            services.TryAddSingleton<ICacheProviderInner, InMemoryCacheProvider>();
            return services;
        }

        // ── DynamoDB ─────────────────────────────────────────────────────────

        /// <summary>Registers the DynamoDB provider.</summary>
        public static IServiceCollection AddCacheWeaveDynamoDb(
            this IServiceCollection services,
            IAmazonDynamoDB dynamoDbClient,
            Action<DynamoDbCacheOptions>? configure = null)
        {
            services.TryAddSingleton(dynamoDbClient);
            if (configure != null)
                services.Configure(configure);
            services.TryAddSingleton<ICacheProviderInner, DynamoDbCacheProvider>();
            return services;
        }

        // ── SQLite ───────────────────────────────────────────────────────────

        /// <summary>Registers the SQLite provider.</summary>
        public static IServiceCollection AddCacheWeaveSQLite(
            this IServiceCollection services,
            Action<SQLiteCacheOptions>? configure = null)
        {
            if (configure != null)
                services.Configure(configure);
            services.TryAddSingleton<ICacheProviderInner, SQLiteCacheProvider>();
            return services;
        }

        // ── NCache ───────────────────────────────────────────────────────────

        /// <summary>Registers the NCache provider.</summary>
        public static IServiceCollection AddCacheWeaveNCache(
            this IServiceCollection services,
            string cacheName)
        {
            services.TryAddSingleton<ICache>(_ => CacheManager.GetCache(cacheName));
            services.TryAddSingleton<ICacheProviderInner, NCacheCacheProvider>();
            return services;
        }
    }
}
