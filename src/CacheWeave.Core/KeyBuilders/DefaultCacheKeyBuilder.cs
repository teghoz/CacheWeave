using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CacheWeave.Core.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CacheWeave.Core.KeyBuilders;

/// <summary>
/// Default key builder. Appends sorted query params for GET-style requests,
/// or a SHA-256 body signature for POST-style requests.
/// </summary>
public sealed class DefaultCacheKeyBuilder : ICacheKeyBuilder
{
    private static readonly JsonSerializerOptions _minifiedOptions = new()
    {
        WriteIndented = false
    };

    public string Build(string baseKey, ActionExecutingContext context, bool includeQueryParams, bool hashBody)
    {
        var segments = new List<string> { baseKey };

        if (includeQueryParams)
        {
            var query = context.HttpContext.Request.Query;
            if (query.Count > 0)
            {
                var sorted = query.Keys
                    .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                    .Select(k => $"{k}={query[k]}");

                segments.Add(string.Join(":", sorted));
            }
        }

        if (hashBody && context.ActionArguments.Count > 0)
        {
            var bodyArg = context.ActionArguments.Values.FirstOrDefault(v => v is not null);
            if (bodyArg is not null)
            {
                var json = JsonSerializer.Serialize(bodyArg, _minifiedOptions);
                var hash = ComputeSha256(json);
                segments.Add(hash);
            }
        }

        return string.Join(":", segments);
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
