using Microsoft.Extensions.Logging;

namespace CacheWeave.Core;

/// <summary>
/// Global configuration for CacheWeave. Registered via <c>AddCacheWeave(options => ...)</c>.
/// All settings here act as defaults; individual <see cref="CacheWeaveAttribute"/> properties
/// can override them per-endpoint.
/// </summary>
public sealed class CacheWeaveOptions
{
    // -------------------------------------------------------------------------
    // Master switch
    // -------------------------------------------------------------------------

    /// <summary>
    /// When <c>false</c>, all cache operations are no-ops: reads always return <c>null</c>
    /// (cache miss), writes and evictions are silently discarded.
    /// Useful for disabling caching at runtime in specific environments (e.g. a staging
    /// environment with a degraded Redis) without changing DI registrations or redeploying.
    /// Default: <c>true</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    // -------------------------------------------------------------------------
    // Key assembly
    // -------------------------------------------------------------------------

    /// <summary>
    /// Separator used between cache key segments.
    /// Default is <c>":"</c> which aligns with Redis namespace conventions.
    /// </summary>
    public string KeySeparator { get; set; } = ":";

    /// <summary>
    /// Optional global key version. When set, it is injected as the second segment
    /// of every cache key, immediately after the base key.
    /// Example: base key "material-types" with version "v2" → "material-types:v2:region=US"
    /// </summary>
    public string? KeyVersion { get; set; }

    // -------------------------------------------------------------------------
    // Expiry / caching behaviour
    // -------------------------------------------------------------------------

    /// <summary>
    /// Default TTL applied to all cache entries when <see cref="CacheWeaveAttribute.ExpirySeconds"/>
    /// is not explicitly set on the attribute. Defaults to 300 seconds (5 minutes).
    /// Set to <c>null</c> for no default expiry.
    /// </summary>
    public TimeSpan? DefaultExpiry { get; set; } = TimeSpan.FromSeconds(300);

    /// <summary>
    /// Global default for <see cref="NoCacheCondition"/>.
    /// Applied when the attribute does not override it.
    /// Default: <see cref="NoCacheCondition.OnErrorOrEmpty"/>.
    /// </summary>
    public NoCacheCondition DefaultNoCacheCondition { get; set; } = NoCacheCondition.OnErrorOrEmpty;

    // -------------------------------------------------------------------------
    // Serialization
    // -------------------------------------------------------------------------

    /// <summary>
    /// Selects the JSON serializer used to serialize and deserialize cached values.
    /// Default: <see cref="CacheWeaveSerializerType.SystemTextJson"/>.
    /// Set to <see cref="CacheWeaveSerializerType.NewtonsoftJson"/> when the host application
    /// already uses Newtonsoft, or when you need features not available in STJ.
    /// </summary>
    public CacheWeaveSerializerType Serializer { get; set; } = CacheWeaveSerializerType.SystemTextJson;

    // -------------------------------------------------------------------------
    // Compression
    // -------------------------------------------------------------------------

    /// <summary>
    /// When true, cache values are GZip-compressed before storage and decompressed on retrieval.
    /// Reduces memory/network usage at the cost of CPU. Recommended for large payloads.
    /// Default: <c>false</c>.
    /// </summary>
    public bool EnableCompression { get; set; } = false;

    // -------------------------------------------------------------------------
    // Observability
    // -------------------------------------------------------------------------

    /// <summary>
    /// When true, CacheWeave emits OpenTelemetry metrics (hits, misses, sets, evictions, duration)
    /// and starts <see cref="System.Diagnostics.Activity"/> spans for each cache operation.
    /// Disable in environments where OTel is not configured to avoid unnecessary overhead.
    /// Default: <c>true</c>.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// The <see cref="LogLevel"/> used for CacheWeave's internal diagnostic messages
    /// (key resolution, hit/miss, write, eviction).
    /// Default: <see cref="LogLevel.Debug"/> — messages are suppressed unless the host
    /// configures <c>CacheWeave.*</c> at Debug or lower.
    /// Set to <see cref="LogLevel.Information"/> to surface cache activity in production logs
    /// without changing the host's log filter configuration.
    /// </summary>
    public LogLevel DiagnosticLogLevel { get; set; } = LogLevel.Debug;
}
