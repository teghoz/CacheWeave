using Microsoft.AspNetCore.Mvc.Filters;

namespace CacheWeave.Core.Abstractions;

/// <summary>
/// Builds a deterministic cache key from an action execution context.
/// </summary>
public interface ICacheKeyBuilder
{
    string Build(string baseKey, ActionExecutingContext context, bool includeQueryParams, bool hashBody);
}
