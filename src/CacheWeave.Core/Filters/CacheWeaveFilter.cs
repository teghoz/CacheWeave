using System.Collections;
using System.Diagnostics;
using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Telemetry;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CacheWeave.Core.Filters;

/// <summary>
/// Action filter that intercepts requests decorated with <see cref="CacheWeaveAttribute"/>,
/// serving cached responses on hit and writing to cache on miss.
/// </summary>
public sealed class CacheWeaveFilter : IAsyncActionFilter
{
    private readonly ICacheProvider _cacheProvider;
    private readonly ICacheKeyBuilder _keyBuilder;
    private readonly ICacheSerializer _serializer;
    private readonly CacheWeaveOptions _options;
    private readonly ILogger<CacheWeaveFilter> _logger;

    public CacheWeaveFilter(
        ICacheProvider cacheProvider,
        ICacheKeyBuilder keyBuilder,
        ICacheSerializer serializer,
        IOptions<CacheWeaveOptions> options,
        ILogger<CacheWeaveFilter> logger)
    {
        _cacheProvider = cacheProvider;
        _keyBuilder = keyBuilder;
        _serializer = serializer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var attribute = (context.ActionDescriptor as ControllerActionDescriptor)
            ?.MethodInfo
            .GetCustomAttributes(typeof(CacheWeaveAttribute), false)
            .OfType<CacheWeaveAttribute>()
            .FirstOrDefault();

        if (attribute is null)
        {
            await next();
            return;
        }

        var cacheKey = await _keyBuilder.BuildAsync(attribute, context);
        Log("CacheWeave: resolving key '{Key}'", cacheKey);

        var sw = _options.EnableMetrics ? Stopwatch.StartNew() : null;
        using var activity = _options.EnableMetrics
            ? CacheWeaveMeter.ActivitySource.StartActivity("CacheWeave.Get")
            : null;
        activity?.SetTag("cache.key", cacheKey);

        string? cached = null;
        try
        {
            cached = await _cacheProvider.GetAsync(cacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "CacheWeave: cache read failed for '{Key}' — falling through to action", cacheKey);
        }

        if (cached is not null)
        {
            sw?.Stop();
            Log("CacheWeave: cache hit for '{Key}'", cacheKey);

            if (_options.EnableMetrics)
            {
                CacheWeaveMeter.Hits.Add(1, new TagList { { "cache.key", cacheKey } });
                CacheWeaveMeter.Duration.Record(
                    sw!.Elapsed.TotalMilliseconds,
                    new TagList { { "cache.key", cacheKey }, { "result", "hit" } });
            }

            activity?.SetTag("cache.hit", true);

            if (attribute.SlidingExpiry)
            {
                var expiry = ResolveExpiry(attribute);
                if (expiry.HasValue)
                {
                    try
                    {
                        await _cacheProvider.SetAsync(cacheKey, cached, expiry);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "CacheWeave: sliding expiry refresh failed for '{Key}' — serving cached response anyway", cacheKey);
                    }
                }
            }

            context.Result = new ContentResult
            {
                Content = cached,
                ContentType = "application/json",
                StatusCode = 200
            };
            return;
        }

        if (_options.EnableMetrics)
            CacheWeaveMeter.Misses.Add(1, new TagList { { "cache.key", cacheKey } });

        activity?.SetTag("cache.hit", false);

        var executed = await next();
        sw?.Stop();

        if (_options.EnableMetrics)
            CacheWeaveMeter.Duration.Record(
                sw!.Elapsed.TotalMilliseconds,
                new TagList { { "cache.key", cacheKey }, { "result", "miss" } });

        if (executed.Exception is not null)
            return;

        var (value, statusCode) = ExtractResult(executed.Result);

        if (value is null)
            return;

        if (!ShouldCache(attribute, value, statusCode))
        {
            Log(
                "CacheWeave: skipping cache write for '{Key}' (status={Status}, condition={Condition})",
                cacheKey, statusCode, attribute.NoCacheWhen);
            return;
        }

        var resolvedExpiry = ResolveExpiry(attribute);
        var serialized = _serializer.Serialize(value, value.GetType());
        try
        {
            await _cacheProvider.SetAsync(cacheKey, serialized, resolvedExpiry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "CacheWeave: cache write failed for '{Key}' — response will not be cached", cacheKey);
            return;
        }

        if (_options.EnableMetrics)
            CacheWeaveMeter.Sets.Add(1, new TagList { { "cache.key", cacheKey } });

        Log("CacheWeave: cached response for '{Key}' (TTL: {Expiry})", cacheKey, resolvedExpiry);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Emits a log message at the level configured in <see cref="CacheWeaveOptions.DiagnosticLogLevel"/>.
    /// </summary>
    private void Log(string message, params object?[] args)
        => _logger.Log(_options.DiagnosticLogLevel, message, args);

    /// <summary>
    /// Extracts the response value and status code from any supported <see cref="IActionResult"/> type.
    /// Covers <see cref="ObjectResult"/> (and all subclasses like <see cref="OkObjectResult"/>,
    /// <see cref="NotFoundObjectResult"/> etc.), <see cref="JsonResult"/>, and raw objects.
    /// </summary>
    private static (object? value, int statusCode) ExtractResult(IActionResult? result)
    {
        return result switch
        {
            ObjectResult obj => (obj.Value, obj.StatusCode ?? 200),
            JsonResult json  => (json.Value, json.StatusCode ?? 200),
            _                => (null, 0)
        };
    }

    private static bool ShouldCache(CacheWeaveAttribute attribute, object value, int statusCode)
    {
        if (attribute.NoCacheWhen == NoCacheCondition.Never)
            return true;

        if (attribute.NoCacheWhen.HasFlag(NoCacheCondition.OnError) &&
            (statusCode < 200 || statusCode >= 300))
            return false;

        if (attribute.NoCacheWhen.HasFlag(NoCacheCondition.OnEmpty) && IsEmpty(value))
            return false;

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
