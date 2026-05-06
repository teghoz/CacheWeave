using Microsoft.AspNetCore.Http;

namespace CacheWeave.Core.Abstractions;

/// <summary>
/// Provides additional context segments to be injected into the cache key.
/// Implement this to add tenant, user, or any other request-scoped isolation.
///
/// Example implementation for tenant isolation:
/// <code>
/// public class TenantKeyContextProvider : IKeyContextProvider
/// {
///     private readonly IHttpContextAccessor _accessor;
///
///     public TenantKeyContextProvider(IHttpContextAccessor accessor)
///         => _accessor = accessor;
///
///     public Task&lt;string?&gt; GetContextSegmentAsync(HttpContext context)
///     {
///         var tenantId = context.User.FindFirst("tenant_id")?.Value;
///         return Task.FromResult(tenantId is not null ? $"tenant={tenantId}" : null);
///     }
/// }
/// </code>
/// Register via: <c>services.AddSingleton&lt;IKeyContextProvider, TenantKeyContextProvider&gt;()</c>
/// </summary>
public interface IKeyContextProvider
{
    /// <summary>
    /// Returns a string segment to inject into the cache key, or <c>null</c> to skip.
    /// The segment is inserted after the version (if any) and before query/body segments.
    /// </summary>
    Task<string?> GetContextSegmentAsync(HttpContext context);
}
