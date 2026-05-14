using CacheWeave.Core.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CacheWeave.Core.Filters;

/// <summary>
/// Minimal API endpoint filter equivalent of <see cref="CacheWeaveFilter"/>.
///
/// Usage on a minimal API endpoint:
/// <code>
/// app.MapGet("/material-types", Handler)
///    .WithCacheWeave("material-vault:material-types", expirySeconds: 300);
///
/// app.MapPost("/material-types/search", Handler)
///    .WithCacheWeave("material-vault:search", hashBody: true);
/// </code>
///
/// Or directly:
/// <code>
/// app.MapGet("/material-types", Handler)
///    .AddEndpointFilter(new CacheWeaveEndpointFilter(
///        new CacheWeaveAttribute("material-vault:material-types"),
///        sp.GetRequiredService&lt;ICacheProvider&gt;(),
///        sp.GetRequiredService&lt;ICacheSerializer&gt;(),
///        sp.GetRequiredService&lt;IOptions&lt;CacheWeaveOptions&gt;&gt;(),
///        sp.GetRequiredService&lt;ILogger&lt;CacheWeaveEndpointFilter&gt;&gt;()));
/// </code>
/// </summary>
public sealed class CacheWeaveEndpointFilter : IEndpointFilter
{
    private readonly CacheWeaveAttribute _attribute;
    private readonly ICacheProvider _cacheProvider;
    private readonly ICacheSerializer _serializer;
    private readonly CacheWeaveOptions _options;
    private readonly ILogger<CacheWeaveEndpointFilter> _logger;

    /// <summary>
    /// Framework-owned route values that are never meaningful for cache key differentiation.
    /// </summary>
    private static readonly HashSet<string> FrameworkRouteKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "controller", "action", "page", "area"
    };

    public CacheWeaveEndpointFilter(
        CacheWeaveAttribute attribute,
        ICacheProvider cacheProvider,
        ICacheSerializer serializer,
        IOptions<CacheWeaveOptions> options,
        ILogger<CacheWeaveEndpointFilter> logger)
    {
        _attribute = attribute;
        _cacheProvider = cacheProvider;
        _serializer = serializer;
        _options = options.Value;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var cacheKey = BuildKey(context.HttpContext);
        _logger.LogDebug("CacheWeave (Endpoint): resolving key '{Key}'", cacheKey);

        string? cached = null;
        try
        {
            cached = await _cacheProvider.GetAsync(cacheKey, context.HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "CacheWeave (Endpoint): cache read failed for '{Key}' — falling through to handler", cacheKey);
        }

        if (cached is not null)
        {
            _logger.LogDebug("CacheWeave (Endpoint): cache hit for '{Key}'", cacheKey);

            if (_attribute.SlidingExpiry)
            {
                var expiry = ResolveExpiry();
                if (expiry.HasValue)
                {
                    try
                    {
                        await _cacheProvider.SetAsync(cacheKey, cached, expiry, context.HttpContext.RequestAborted);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "CacheWeave (Endpoint): sliding expiry refresh failed for '{Key}' — serving cached response anyway", cacheKey);
                    }
                }
            }

            return Results.Text(cached, "application/json");
        }

        var result = await next(context);

        if (result is not null)
        {
            var serialized = _serializer.Serialize(result, result.GetType());
            var resolvedExpiry = ResolveExpiry();
            try
            {
                await _cacheProvider.SetAsync(cacheKey, serialized, resolvedExpiry, context.HttpContext.RequestAborted);
                _logger.LogDebug("CacheWeave (Endpoint): cached response for '{Key}' (TTL: {Expiry})", cacheKey, resolvedExpiry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "CacheWeave (Endpoint): cache write failed for '{Key}' — response will not be cached", cacheKey);
            }
        }

        return result;
    }

    private string BuildKey(HttpContext httpContext)
    {
        var sep = _options.KeySeparator;
        var segments = new List<string> { _attribute.Key };

        if (!string.IsNullOrWhiteSpace(_options.KeyVersion))
            segments.Add(_options.KeyVersion);

        if (_attribute.IncludeRouteParams)
        {
            var routeValues = httpContext.Request.RouteValues;
            if (routeValues.Count > 0)
            {
                var excludedRoute = _attribute.ExcludeRouteParams.Length > 0
                    ? new HashSet<string>(_attribute.ExcludeRouteParams, StringComparer.OrdinalIgnoreCase)
                    : null;

                var routeSegments = routeValues
                    .Where(kv => !FrameworkRouteKeys.Contains(kv.Key)
                                 && (excludedRoute is null || !excludedRoute.Contains(kv.Key))
                                 && kv.Value is not null)
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => $"{kv.Key}={kv.Value}")
                    .ToList();

                if (routeSegments.Count > 0)
                    segments.Add(string.Join(sep, routeSegments));
            }
        }

        if (_attribute.IncludeQueryParams)
        {
            var query = httpContext.Request.Query;
            var excluded = _attribute.ExcludeParams.Length > 0
                ? new HashSet<string>(_attribute.ExcludeParams, StringComparer.OrdinalIgnoreCase)
                : null;

            var paramSegments = query.Keys
                .Where(k => excluded is null || !excluded.Contains(k))
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .Select(k => $"{k}={query[k]}")
                .ToList();

            if (paramSegments.Count > 0)
                segments.Add(string.Join(sep, paramSegments));
        }

        var key = string.Join(sep, segments);

        if (!string.IsNullOrWhiteSpace(_options.GlobalKeyPrefix))
            key = $"{_options.GlobalKeyPrefix}{sep}{key}";

        return key;
    }

    private TimeSpan? ResolveExpiry()
    {
        if (_attribute.ExpirySeconds == 0) return null;
        if (_attribute.ExpirySeconds > 0) return TimeSpan.FromSeconds(_attribute.ExpirySeconds);
        return _options.DefaultExpiry;
    }
}
