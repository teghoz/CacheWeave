namespace CacheWeave.Core;

/// <summary>
/// Decorates an ASP.NET Core action to cache its response using the configured CacheWeave provider.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class CacheWeaveAttribute : Attribute
{
    /// <summary>
    /// The base cache key. Query param segments or a body hash will be appended automatically.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// When true, all query string parameters are appended to the cache key in sorted order.
    /// </summary>
    public bool IncludeQueryParams { get; set; } = true;

    /// <summary>
    /// When true, the request body is serialized, minified, and SHA-256 hashed.
    /// The resulting signature is appended to the cache key. Intended for POST endpoints.
    /// </summary>
    public bool HashBody { get; set; } = false;

    /// <summary>
    /// Cache entry TTL in seconds. Defaults to 300 (5 minutes). Set to -1 for no expiry.
    /// </summary>
    public int ExpirySeconds { get; set; } = 300;

    public CacheWeaveAttribute(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Cache key must not be null or empty.", nameof(key));

        Key = key;
    }
}
