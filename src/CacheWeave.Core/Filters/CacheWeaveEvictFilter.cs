using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Telemetry;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CacheWeave.Core.Filters;

/// <summary>
/// Action filter that processes <see cref="CacheWeaveEvictAttribute"/> decorations,
/// evicting cache entries after a write action executes.
/// Supports multiple eviction attributes on a single action.
/// </summary>
public sealed class CacheWeaveEvictFilter : IAsyncActionFilter
{
    private readonly ICacheProvider _cacheProvider;
    private readonly CacheWeaveOptions _options;
    private readonly ILogger<CacheWeaveEvictFilter> _logger;

    public CacheWeaveEvictFilter(
        ICacheProvider cacheProvider,
        IOptions<CacheWeaveOptions> options,
        ILogger<CacheWeaveEvictFilter> logger)
    {
        _cacheProvider = cacheProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var attributes = (context.ActionDescriptor as ControllerActionDescriptor)
            ?.MethodInfo
            .GetCustomAttributes(typeof(CacheWeaveEvictAttribute), false)
            .OfType<CacheWeaveEvictAttribute>()
            .ToList();

        if (attributes is null || attributes.Count == 0)
        {
            await next();
            return;
        }

        var executed = await next();

        var isSuccess = executed.Exception is null &&
                        (executed.Result is not Microsoft.AspNetCore.Mvc.ObjectResult result ||
                         result.StatusCode is null or >= 200 and < 300);

        foreach (var attribute in attributes)
        {
            if (!isSuccess && !attribute.EvictOnFailure)
            {
                Log(
                    "CacheWeave: skipping eviction for '{KeyOrPrefix}' — action did not succeed",
                    attribute.Key ?? attribute.Prefix);
                continue;
            }

            if (attribute.Key is not null)
            {
                Log("CacheWeave: evicting key '{Key}'", attribute.Key);
                await _cacheProvider.RemoveAsync(attribute.Key);

                if (_options.EnableMetrics)
                    CacheWeaveMeter.Evictions.Add(1, new System.Diagnostics.TagList { { "cache.key", attribute.Key } });
            }
            else if (attribute.Prefix is not null)
            {
                Log("CacheWeave: evicting by prefix '{Prefix}'", attribute.Prefix);
                await _cacheProvider.RemoveByPrefixAsync(attribute.Prefix);

                if (_options.EnableMetrics)
                    CacheWeaveMeter.Evictions.Add(1, new System.Diagnostics.TagList { { "cache.prefix", attribute.Prefix } });
            }
            else
            {
                _logger.LogWarning(
                    "CacheWeave: {Attribute} on '{Action}' has neither Key nor Prefix set — skipping",
                    nameof(CacheWeaveEvictAttribute),
                    (context.ActionDescriptor as ControllerActionDescriptor)?.ActionName);
            }
        }
    }

    private void Log(string message, params object?[] args)
        => _logger.Log(_options.DiagnosticLogLevel, message, args);
}
