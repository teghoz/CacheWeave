using System.Collections;
using CacheWeave.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CacheWeave.Core.Filters;

/// <summary>
/// Razor Pages equivalent of <see cref="CacheWeaveFilter"/>.
/// Apply <see cref="CacheWeaveAttribute"/> to <c>OnGet</c> / <c>OnPost</c> handler methods
/// on a <see cref="PageModel"/> to cache their responses.
///
/// Register globally:
/// <code>
/// builder.Services.AddRazorPages().AddMvcOptions(o =>
/// {
///     o.Filters.AddService&lt;CacheWeavePageFilter&gt;();
/// });
/// </code>
/// </summary>
public sealed class CacheWeavePageFilter : IAsyncPageFilter
{
    private readonly ICacheProvider _cacheProvider;
    private readonly ICacheSerializer _serializer;
    private readonly CacheWeaveOptions _options;
    private readonly ILogger<CacheWeavePageFilter> _logger;

    public CacheWeavePageFilter(
        ICacheProvider cacheProvider,
        ICacheSerializer serializer,
        IOptions<CacheWeaveOptions> options,
        ILogger<CacheWeavePageFilter> logger)
    {
        _cacheProvider = cacheProvider;
        _serializer = serializer;
        _options = options.Value;
        _logger = logger;
    }

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(
        PageHandlerExecutingContext context,
        PageHandlerExecutionDelegate next)
    {
        var attribute = context.HandlerMethod?.MethodInfo
            .GetCustomAttributes(typeof(CacheWeaveAttribute), false)
            .OfType<CacheWeaveAttribute>()
            .FirstOrDefault();

        if (attribute is null)
        {
            await next();
            return;
        }

        // Build key from base key + query params (Razor Pages don't have action arguments for body)
        var sep = _options.KeySeparator;
        var segments = new List<string> { attribute.Key };

        if (!string.IsNullOrWhiteSpace(_options.KeyVersion))
            segments.Add(_options.KeyVersion);

        if (attribute.IncludeQueryParams)
        {
            var query = context.HttpContext.Request.Query;
            var excluded = attribute.ExcludeParams.Length > 0
                ? new HashSet<string>(attribute.ExcludeParams, StringComparer.OrdinalIgnoreCase)
                : null;

            var paramSegments = query.Keys
                .Where(k => excluded is null || !excluded.Contains(k))
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .Select(k => $"{k}={query[k]}")
                .ToList();

            if (paramSegments.Count > 0)
                segments.Add(string.Join(sep, paramSegments));
        }

        var cacheKey = string.Join(sep, segments);
        _logger.LogDebug("CacheWeave (Page): resolving key '{Key}'", cacheKey);

        var cached = await _cacheProvider.GetAsync(cacheKey);
        if (cached is not null)
        {
            _logger.LogDebug("CacheWeave (Page): cache hit for '{Key}'", cacheKey);

            if (attribute.SlidingExpiry)
            {
                var expiry = ResolveExpiry(attribute);
                if (expiry.HasValue)
                    await _cacheProvider.SetAsync(cacheKey, cached, expiry);
            }

            context.Result = new ContentResult
            {
                Content = cached,
                ContentType = "application/json",
                StatusCode = 200
            };
            return;
        }

        var executed = await next();

        if (executed.Exception is not null || executed.Result is null)
            return;

        var (value, statusCode) = executed.Result switch
        {
            ObjectResult obj => (obj.Value, obj.StatusCode ?? 200),
            JsonResult json  => (json.Value, json.StatusCode ?? 200),
            _                => ((object?)null, 0)
        };

        if (value is null || !ShouldCache(attribute, value, statusCode))
            return;

        var resolvedExpiry = ResolveExpiry(attribute);
        var serialized = _serializer.Serialize(value, value.GetType());
        await _cacheProvider.SetAsync(cacheKey, serialized, resolvedExpiry);
        _logger.LogDebug("CacheWeave (Page): cached response for '{Key}' (TTL: {Expiry})", cacheKey, resolvedExpiry);
    }

    private static bool ShouldCache(CacheWeaveAttribute attribute, object value, int statusCode)
    {
        if (attribute.NoCacheWhen == NoCacheCondition.Never) return true;
        if (attribute.NoCacheWhen.HasFlag(NoCacheCondition.OnError) && (statusCode < 200 || statusCode >= 300)) return false;
        if (attribute.NoCacheWhen.HasFlag(NoCacheCondition.OnEmpty) && IsEmpty(value)) return false;
        return true;
    }

    private static bool IsEmpty(object value) => value switch
    {
        string s      => string.IsNullOrEmpty(s),
        ICollection c => c.Count == 0,
        IEnumerable e => !e.Cast<object>().Any(),
        _             => false
    };

    private TimeSpan? ResolveExpiry(CacheWeaveAttribute attribute)
    {
        if (attribute.ExpirySeconds == 0) return null;
        if (attribute.ExpirySeconds > 0) return TimeSpan.FromSeconds(attribute.ExpirySeconds);
        return _options.DefaultExpiry;
    }
}
