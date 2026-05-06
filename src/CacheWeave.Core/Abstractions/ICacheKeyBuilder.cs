using Microsoft.AspNetCore.Mvc.Filters;

namespace CacheWeave.Core.Abstractions;

/// <summary>
/// Builds a deterministic cache key from an action execution context and attribute configuration.
/// </summary>
public interface ICacheKeyBuilder
{
    Task<string> BuildAsync(CacheWeaveAttribute attribute, ActionExecutingContext context);
}
