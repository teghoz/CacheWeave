using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CacheWeave.Core.Telemetry;

/// <summary>
/// OpenTelemetry-compatible metrics and activity source for CacheWeave.
/// Instruments are automatically picked up by any OTel SDK configured in the host.
///
/// Metrics emitted:
///   cacheweave.hits          — Counter: number of cache hits
///   cacheweave.misses        — Counter: number of cache misses
///   cacheweave.sets          — Counter: number of cache writes
///   cacheweave.evictions     — Counter: number of explicit evictions
///   cacheweave.duration      — Histogram (ms): time to resolve a cache key (hit or miss + factory)
///
/// Tags on all instruments:
///   cache.key  — the resolved cache key
///   provider   — the provider type name (e.g. "RedisCacheProvider")
/// </summary>
public static class CacheWeaveMeter
{
    public const string MeterName = "CacheWeave";
    public const string ActivitySourceName = "CacheWeave";

    private static readonly Meter _meter = new(MeterName, "1.0.0");

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");

    public static readonly Counter<long> Hits =
        _meter.CreateCounter<long>("cacheweave.hits", description: "Number of cache hits");

    public static readonly Counter<long> Misses =
        _meter.CreateCounter<long>("cacheweave.misses", description: "Number of cache misses");

    public static readonly Counter<long> Sets =
        _meter.CreateCounter<long>("cacheweave.sets", description: "Number of cache writes");

    public static readonly Counter<long> Evictions =
        _meter.CreateCounter<long>("cacheweave.evictions", description: "Number of explicit cache evictions");

    public static readonly Histogram<double> Duration =
        _meter.CreateHistogram<double>(
            "cacheweave.duration",
            unit: "ms",
            description: "Time in milliseconds to resolve a cache operation");
}
