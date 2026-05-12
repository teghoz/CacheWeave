using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CacheWeave.Core.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace CacheWeave.Core.KeyBuilders;

/// <summary>
/// Default cache key builder. Assembles keys in the following segment order:
///
///   {baseKey}{sep}{version?}{sep}{contextSegment?}{sep}{routeParams?}{sep}{queryParams?}{sep}{bodyHash?}
///
/// Example (Redis convention, version "v2", tenant scoped, GET with route param):
///   material-vault:material-types:v2:tenant=acme:id=42:page=1:pageSize=20:region=US
///
/// Example (POST with selective body hash):
///   material-vault:search:v2:tenant=acme:a3f9c2d1e4b7...
/// </summary>
    public sealed class DefaultCacheKeyBuilder : ICacheKeyBuilder
{
    private readonly CacheWeaveOptions _options;
    private readonly IKeyContextProvider? _contextProvider;
    private readonly ICacheSerializer _serializer;

    /// <summary>
    /// Framework-owned route values that are never meaningful for cache key differentiation.
    /// </summary>
    private static readonly HashSet<string> FrameworkRouteKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "controller", "action", "page", "area"
    };

    // Always use minified JSON for hashing regardless of the configured serializer
    private static readonly JsonSerializerOptions _minifiedOptions = new()
    {
        WriteIndented = false
    };

    public DefaultCacheKeyBuilder(
        IOptions<CacheWeaveOptions> options,
        ICacheSerializer serializer,
        IKeyContextProvider? contextProvider = null)
    {
        _options = options.Value;
        _serializer = serializer;
        _contextProvider = contextProvider;
    }

    public async Task<string> BuildAsync(CacheWeaveAttribute attribute, ActionExecutingContext context)
    {
        var sep = _options.KeySeparator;
        var baseKey = ResolveBaseKey(attribute, context);
        var segments = new List<string> { baseKey };

        // 1. Global version segment
        if (!string.IsNullOrWhiteSpace(_options.KeyVersion))
            segments.Add(_options.KeyVersion);

        // 2. Context segment (tenant, user, etc.) — optional, provided by consumer
        if (_contextProvider is not null)
        {
            var contextSegment = await _contextProvider.GetContextSegmentAsync(context.HttpContext);
            if (!string.IsNullOrWhiteSpace(contextSegment))
                segments.Add(contextSegment);
        }

        // 3. Route param segments
        if (attribute.IncludeRouteParams)
        {
            var routeValues = context.HttpContext.Request.RouteValues;
            if (routeValues.Count > 0)
            {
                var excludedRoute = attribute.ExcludeRouteParams.Length > 0
                    ? new HashSet<string>(attribute.ExcludeRouteParams, StringComparer.OrdinalIgnoreCase)
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

        // 4. Query param segments
        if (attribute.IncludeQueryParams)
        {
            var query = context.HttpContext.Request.Query;
            if (query.Count > 0)
            {
                var excluded = attribute.ExcludeParams.Length > 0
                    ? new HashSet<string>(attribute.ExcludeParams, StringComparer.OrdinalIgnoreCase)
                    : null;

                var paramSegment = query.Keys
                    .Where(k => excluded is null || !excluded.Contains(k))
                    .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                    .Select(k => $"{k}={query[k]}")
                    .ToList();

                if (paramSegment.Count > 0)
                    segments.Add(string.Join(sep, paramSegment));
            }
        }

        // 5. Body hash segment
        if (attribute.HashBody && context.ActionArguments.Count > 0)
        {
            var bodyArg = context.ActionArguments.Values.FirstOrDefault(v => v is not null);
            if (bodyArg is not null)
            {
                var hash = ComputeBodyHash(bodyArg, attribute.HashBodyFields);
                segments.Add(hash);
            }
        }

        var key = string.Join(sep, segments);

        // 6. Global key prefix — prepended last so it wraps the entire assembled key
        if (!string.IsNullOrWhiteSpace(_options.GlobalKeyPrefix))
            key = $"{_options.GlobalKeyPrefix}{sep}{key}";

        return key;
    }

    /// <summary>
    /// Returns the explicit <see cref="CacheWeaveAttribute.Key"/> when set, otherwise derives
    /// a stable key from the controller and action name via the action descriptor.
    /// Format when derived: <c>{ControllerName}.{ActionName}</c>
    /// e.g. <c>MaterialCategories.GetAll</c>
    /// </summary>
    private static string ResolveBaseKey(CacheWeaveAttribute attribute, ActionExecutingContext context)
    {
        if (!string.IsNullOrWhiteSpace(attribute.Key))
            return attribute.Key;

        if (context.ActionDescriptor is ControllerActionDescriptor descriptor)
            return $"{descriptor.ControllerName}.{descriptor.ActionName}";

        // Minimal API or Razor Page fallback — use the display name
        var displayName = context.ActionDescriptor.DisplayName;
        return string.IsNullOrWhiteSpace(displayName)
            ? "unknown"
            : displayName;
    }

    private static string ComputeBodyHash(object body, string[] fields)
    {
        string json;

        if (fields.Length > 0)
        {
            // Serialize full body first, then extract only the specified fields
            var fullJson = JsonSerializer.Serialize(body, _minifiedOptions);
            var node = JsonNode.Parse(fullJson);

            if (node is JsonObject obj)
            {
                var filtered = new JsonObject();
                foreach (var field in fields.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    if (obj.TryGetPropertyValue(field, out var value))
                        filtered[field] = value?.DeepClone();
                }
                json = filtered.ToJsonString(_minifiedOptions);
            }
            else
            {
                // Body is not an object (e.g. array or primitive) — hash as-is
                json = fullJson;
            }
        }
        else
        {
            // Hash entire body — serialize and strip whitespace
            json = JsonSerializer.Serialize(body, _minifiedOptions);
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
