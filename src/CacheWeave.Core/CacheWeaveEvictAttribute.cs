namespace CacheWeave.Core;

/// <summary>
/// Decorates a write action (POST, PUT, PATCH, DELETE) to evict cache entries
/// after the action executes successfully.
///
/// Supports exact key eviction and prefix-based eviction.
///
/// <example>
/// Evict a single key:
/// <code>
/// [CacheWeaveEvict(Key = "material-vault:material-types")]
/// [HttpPost]
/// public async Task&lt;IActionResult&gt; CreateMaterialType(...) { }
/// </code>
///
/// Evict all keys under a prefix (e.g. after any write to material-types):
/// <code>
/// [CacheWeaveEvict(Prefix = "material-vault:material-types")]
/// [HttpDelete("{id}")]
/// public async Task&lt;IActionResult&gt; DeleteMaterialType(int id) { }
/// </code>
/// </example>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class CacheWeaveEvictAttribute : Attribute
{
    /// <summary>
    /// Exact cache key to evict. Mutually exclusive with <see cref="Prefix"/>.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    /// Cache key prefix to evict. All keys beginning with this value will be removed.
    /// Mutually exclusive with <see cref="Key"/>.
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>
    /// When true, eviction runs even if the action returned a non-success status code.
    /// Default: <c>false</c> — eviction only runs on successful responses (2xx).
    /// </summary>
    public bool EvictOnFailure { get; set; } = false;

    public CacheWeaveEvictAttribute()
    {
    }

    /// <summary>Convenience constructor for exact key eviction.</summary>
    public CacheWeaveEvictAttribute(string key)
    {
        Key = key;
    }
}
