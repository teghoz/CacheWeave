using System.Text.Json;
using CacheWeave.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace CacheWeave.Core.Filters;

/// <summary>
/// Action filter that intercepts requests decorated with <see cref="CacheWeaveAttribute"/>,
/// serving cached responses on hit and writing to cache on miss.
/// </summary>
public sealed class CacheWeaveFilter : IAsyncActionFilter
{
    private readonly ICacheProvider _cacheProvider;
    private readonly ICacheKeyBuilder _keyBuilder;
    private readonly ILogger<CacheWeaveFilter> _logger;

    public CacheWeaveFilter(
        ICacheProvider cacheProvider,
        ICacheKeyBuilder keyBuilder,
        ILogger<CacheWeaveFilter> logger)
    {
        _cacheProvider = cacheProvider;
        _keyBuilder = keyBuilder;
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

        var cacheKey = _keyBuilder.Build(
            attribute.Key,
            context,
            attribute.IncludeQueryParams,
            attribute.HashBody);

        _logger.LogDebug("CacheWeave: resolving key '{Key}'", cacheKey);

        var cached = await _cacheProvider.GetAsync(cacheKey);
        if (cached is not null)
        {
            _logger.LogDebug("CacheWeave: cache hit for '{Key}'", cacheKey);
            context.Result = new ContentResult
            {
                Content = cached,
                ContentType = "application/json",
                StatusCode = 200
            };
            return;
        }

        var executed = await next();

        if (executed.Result is ObjectResult { Value: not null } objectResult)
        {
            var serialized = JsonSerializer.Serialize(objectResult.Value);
            var expiry = attribute.ExpirySeconds >= 0
                ? TimeSpan.FromSeconds(attribute.ExpirySeconds)
                : (TimeSpan?)null;

            await _cacheProvider.SetAsync(cacheKey, serialized, expiry);
            _logger.LogDebug("CacheWeave: cached response for '{Key}' (TTL: {Expiry})", cacheKey, expiry);
        }
    }
}
