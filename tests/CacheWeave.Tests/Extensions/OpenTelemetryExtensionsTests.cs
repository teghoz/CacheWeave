using CacheWeave.Core.Extensions;
using CacheWeave.Core.Telemetry;
using FluentAssertions;
using Xunit;

namespace CacheWeave.Tests.Extensions;

public class OpenTelemetryExtensionsTests
{
    // -------------------------------------------------------------------------
    // Fake builders that record what was invoked on them
    // -------------------------------------------------------------------------

    private sealed class FakeMeterBuilder
    {
        public List<string[]> AddMeterCalls { get; } = [];

        public FakeMeterBuilder AddMeter(string[] names)
        {
            AddMeterCalls.Add(names);
            return this;
        }
    }

    private sealed class FakeTracerBuilder
    {
        public List<string[]> AddSourceCalls { get; } = [];

        public FakeTracerBuilder AddSource(string[] names)
        {
            AddSourceCalls.Add(names);
            return this;
        }
    }

    // A builder that has neither AddMeter nor AddSource — reflection returns null
    private sealed class EmptyBuilder { }

    // -------------------------------------------------------------------------
    // AddCacheWeaveMeter
    // -------------------------------------------------------------------------

    [Fact]
    public void AddCacheWeaveMeter_InvokesMeterMethod_WhenPresent()
    {
        var builder = new FakeMeterBuilder();

        var returned = builder.AddCacheWeaveMeter();

        returned.Should().BeSameAs(builder);
        builder.AddMeterCalls.Should().HaveCount(1);
        builder.AddMeterCalls[0].Should().Contain(CacheWeaveMeter.MeterName);
    }

    [Fact]
    public void AddCacheWeaveMeter_DoesNotThrow_WhenMethodMissing()
    {
        var builder = new EmptyBuilder();

        var act = () => builder.AddCacheWeaveMeter();

        act.Should().NotThrow();
    }

    [Fact]
    public void AddCacheWeaveMeter_ReturnsBuilder_WhenMethodMissing()
    {
        var builder = new EmptyBuilder();

        var returned = builder.AddCacheWeaveMeter();

        returned.Should().BeSameAs(builder);
    }

    // -------------------------------------------------------------------------
    // AddCacheWeaveInstrumentation
    // -------------------------------------------------------------------------

    [Fact]
    public void AddCacheWeaveInstrumentation_InvokesAddSource_WhenPresent()
    {
        var builder = new FakeTracerBuilder();

        var returned = builder.AddCacheWeaveInstrumentation();

        returned.Should().BeSameAs(builder);
        builder.AddSourceCalls.Should().HaveCount(1);
        builder.AddSourceCalls[0].Should().Contain(CacheWeaveMeter.ActivitySourceName);
    }

    [Fact]
    public void AddCacheWeaveInstrumentation_DoesNotThrow_WhenMethodMissing()
    {
        var builder = new EmptyBuilder();

        var act = () => builder.AddCacheWeaveInstrumentation();

        act.Should().NotThrow();
    }

    [Fact]
    public void AddCacheWeaveInstrumentation_ReturnsBuilder_WhenMethodMissing()
    {
        var builder = new EmptyBuilder();

        var returned = builder.AddCacheWeaveInstrumentation();

        returned.Should().BeSameAs(builder);
    }
}
