using CacheWeave.Core.Telemetry;
using Microsoft.Extensions.DependencyInjection;

namespace CacheWeave.Core.Extensions;

/// <summary>
/// Extension methods for wiring CacheWeave telemetry into OpenTelemetry.
/// </summary>
public static class OpenTelemetryExtensions
{
    /// <summary>
    /// Adds CacheWeave metrics to an OpenTelemetry <c>MeterProviderBuilder</c>.
    /// Call this inside <c>WithMetrics(b => b.AddCacheWeaveMeter())</c>.
    /// </summary>
    public static TBuilder AddCacheWeaveMeter<TBuilder>(this TBuilder builder)
        where TBuilder : class
    {
        // Reflection-based to avoid a hard dependency on OpenTelemetry SDK packages in Core.
        // If the consumer has OTel configured, this will wire up automatically.
        var addMeterMethod = builder.GetType().GetMethod("AddMeter", [typeof(string[])]);
        addMeterMethod?.Invoke(builder, [new[] { CacheWeaveMeter.MeterName }]);
        return builder;
    }

    /// <summary>
    /// Adds CacheWeave activity tracing to an OpenTelemetry <c>TracerProviderBuilder</c>.
    /// Call this inside <c>WithTracing(b => b.AddCacheWeaveInstrumentation())</c>.
    /// </summary>
    public static TBuilder AddCacheWeaveInstrumentation<TBuilder>(this TBuilder builder)
        where TBuilder : class
    {
        var addSourceMethod = builder.GetType().GetMethod("AddSource", [typeof(string[])]);
        addSourceMethod?.Invoke(builder, [new[] { CacheWeaveMeter.ActivitySourceName }]);
        return builder;
    }
}
