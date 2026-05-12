using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Filters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CacheWeave.Core.Extensions;

/// <summary>
/// Extension methods for applying CacheWeave to Minimal API endpoints.
/// </summary>
public static class RouteHandlerBuilderExtensions
{
    /// <summary>
    /// Applies CacheWeave caching to a Minimal API endpoint.
    /// </summary>
    /// <param name="builder">The route handler builder.</param>
    /// <param name="key">The base cache key.</param>
    /// <param name="expirySeconds">TTL in seconds. -1 uses the global default.</param>
    /// <param name="includeRouteParams">Whether to append route params (path segments) to the key.</param>
    /// <param name="excludeRouteParams">Route param names to exclude from the key.</param>
    /// <param name="includeQueryParams">Whether to append query params to the key.</param>
    /// <param name="excludeParams">Query param names to exclude from the key.</param>
    /// <param name="hashBody">Whether to hash the request body (for POST endpoints).</param>
    /// <param name="hashBodyFields">Specific body fields to hash. Empty = hash entire body.</param>
    /// <param name="slidingExpiry">Whether to reset TTL on each cache hit.</param>
    /// <param name="noCacheWhen">Conditions under which the response should not be cached.</param>
    public static RouteHandlerBuilder WithCacheWeave(
        this RouteHandlerBuilder builder,
        string key,
        int expirySeconds = -1,
        bool includeRouteParams = true,
        string[]? excludeRouteParams = null,
        bool includeQueryParams = true,
        string[]? excludeParams = null,
        bool hashBody = false,
        string[]? hashBodyFields = null,
        bool slidingExpiry = false,
        NoCacheCondition noCacheWhen = NoCacheCondition.OnErrorOrEmpty)
    {
        var attribute = new CacheWeaveAttribute(key)
        {
            ExpirySeconds = expirySeconds,
            IncludeRouteParams = includeRouteParams,
            ExcludeRouteParams = excludeRouteParams ?? [],
            IncludeQueryParams = includeQueryParams,
            ExcludeParams = excludeParams ?? [],
            HashBody = hashBody,
            HashBodyFields = hashBodyFields ?? [],
            SlidingExpiry = slidingExpiry,
            NoCacheWhen = noCacheWhen
        };

        builder.AddEndpointFilter((context, next) =>
        {
            var sp = context.HttpContext.RequestServices;
            var filter = new CacheWeaveEndpointFilter(
                attribute,
                sp.GetRequiredService<ICacheProvider>(),
                sp.GetRequiredService<ICacheSerializer>(),
                sp.GetRequiredService<IOptions<CacheWeaveOptions>>(),
                sp.GetRequiredService<ILogger<CacheWeaveEndpointFilter>>());

            return filter.InvokeAsync(context, next);
        });

        return builder;
    }
}
