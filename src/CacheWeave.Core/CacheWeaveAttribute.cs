namespace CacheWeave.Core;

/// <summary>
/// Decorates an ASP.NET Core action to cache its response using the configured CacheWeave provider.
/// </summary>
/// <remarks>
/// <para>
/// The <paramref name="key"/> constructor parameter is optional. When omitted (or when the
/// parameterless constructor is used), the cache key base is derived automatically from the
/// controller and action name via reflection:
/// <c>{ControllerName}.{ActionName}</c> (e.g. <c>MaterialCategories.GetAll</c>).
/// </para>
/// <para>
/// Additional key segments (version, context, query params, body hash) are always appended
/// according to global <see cref="CacheWeaveOptions"/> and per-attribute settings.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class CacheWeaveAttribute : Attribute
{
    /// <summary>
    /// The explicit base cache key. When <c>null</c>, the key builder derives the base key
    /// from the controller and action name via reflection.
    /// </summary>
    public string? Key { get; }

    /// <summary>
    /// When true, route parameters (path segments such as <c>{id}</c>) are appended to the
    /// cache key in sorted order. Built-in framework route values (<c>controller</c>,
    /// <c>action</c>, <c>page</c>, <c>area</c>) are always excluded automatically.
    /// Parameters listed in <see cref="ExcludeRouteParams"/> are also excluded.
    /// Default: <c>true</c>.
    /// </summary>
    public bool IncludeRouteParams { get; set; } = true;

    /// <summary>
    /// Route parameter names to exclude from the cache key.
    /// Useful for stripping route values that do not affect the response.
    /// When empty, all route params are included (subject to <see cref="IncludeRouteParams"/>).
    /// Framework route values (<c>controller</c>, <c>action</c>, <c>page</c>, <c>area</c>)
    /// are always excluded regardless of this setting.
    /// </summary>
    public string[] ExcludeRouteParams { get; set; } = [];

    /// <summary>
    /// When true, query string parameters are appended to the cache key in sorted order.
    /// Parameters listed in <see cref="ExcludeParams"/> are excluded.
    /// Default: <c>true</c>.
    /// </summary>
    public bool IncludeQueryParams { get; set; } = true;

    /// <summary>
    /// Query parameter names to exclude from the cache key.
    /// Useful for stripping noise params (tracking IDs, debug flags, correlation headers)
    /// that do not affect the response.
    /// When empty, all query params are included (subject to <see cref="IncludeQueryParams"/>).
    /// </summary>
    public string[] ExcludeParams { get; set; } = [];

    /// <summary>
    /// When true, the request body is serialized, minified, and SHA-256 hashed.
    /// The resulting signature is appended to the cache key. Intended for POST endpoints.
    /// </summary>
    public bool HashBody { get; set; } = false;

    /// <summary>
    /// When <see cref="HashBody"/> is true and this array is non-empty, only the specified
    /// top-level fields are extracted from the body before hashing.
    /// This allows stable cache keys even when the body contains noise fields
    /// (e.g. <c>requestId</c>, <c>timestamp</c>, <c>correlationId</c>).
    /// When empty, the entire body is serialized and hashed.
    /// </summary>
    public string[] HashBodyFields { get; set; } = [];

    /// <summary>
    /// Cache entry TTL in seconds. Defaults to -1, which means the global
    /// <see cref="CacheWeaveOptions.DefaultExpiry"/> is used.
    /// Set to 0 for no expiry on this specific endpoint.
    /// </summary>
    public int ExpirySeconds { get; set; } = -1;

    /// <summary>
    /// When true, the cache entry TTL is reset on every cache hit (sliding expiration).
    /// Default: <c>false</c> (absolute expiration).
    /// Note: not all providers support sliding expiry natively — the filter emulates it
    /// by re-writing the entry on each hit.
    /// </summary>
    public bool SlidingExpiry { get; set; } = false;

    /// <summary>
    /// Controls when caching is skipped for a response.
    /// Default: <see cref="NoCacheCondition.OnErrorOrEmpty"/> — does not cache
    /// error responses (non-2xx) or empty/null results.
    /// </summary>
    public NoCacheCondition NoCacheWhen { get; set; } = NoCacheCondition.OnErrorOrEmpty;

    /// <summary>
    /// Initialises the attribute with an explicit base cache key.
    /// </summary>
    /// <param name="key">
    /// The base cache key. Must not be null or whitespace.
    /// Additional segments are appended automatically.
    /// </param>
    public CacheWeaveAttribute(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Cache key must not be null or empty.", nameof(key));
        Key = key;
    }

    /// <summary>
    /// Initialises the attribute without an explicit key.
    /// The key builder will derive the base key from the controller and action name at runtime.
    /// </summary>
    public CacheWeaveAttribute()
    {
        Key = null;
    }
}
