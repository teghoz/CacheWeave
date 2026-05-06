using CacheWeave.Core.Telemetry;
using FluentAssertions;
using Xunit;

namespace CacheWeave.Tests.Telemetry;

public class CacheWeaveMeterTests
{
    [Fact]
    public void MeterName_IsExpectedValue()
        => CacheWeaveMeter.MeterName.Should().Be("CacheWeave");

    [Fact]
    public void ActivitySourceName_IsExpectedValue()
        => CacheWeaveMeter.ActivitySourceName.Should().Be("CacheWeave");

    [Fact]
    public void ActivitySource_IsNotNull()
        => CacheWeaveMeter.ActivitySource.Should().NotBeNull();

    [Fact]
    public void Hits_Counter_IsNotNull()
        => CacheWeaveMeter.Hits.Should().NotBeNull();

    [Fact]
    public void Misses_Counter_IsNotNull()
        => CacheWeaveMeter.Misses.Should().NotBeNull();

    [Fact]
    public void Sets_Counter_IsNotNull()
        => CacheWeaveMeter.Sets.Should().NotBeNull();

    [Fact]
    public void Evictions_Counter_IsNotNull()
        => CacheWeaveMeter.Evictions.Should().NotBeNull();

    [Fact]
    public void Duration_Histogram_IsNotNull()
        => CacheWeaveMeter.Duration.Should().NotBeNull();

    [Fact]
    public void Hits_Add_DoesNotThrow()
    {
        var act = () => CacheWeaveMeter.Hits.Add(1);
        act.Should().NotThrow();
    }

    [Fact]
    public void Misses_Add_DoesNotThrow()
    {
        var act = () => CacheWeaveMeter.Misses.Add(1);
        act.Should().NotThrow();
    }

    [Fact]
    public void Sets_Add_DoesNotThrow()
    {
        var act = () => CacheWeaveMeter.Sets.Add(1);
        act.Should().NotThrow();
    }

    [Fact]
    public void Evictions_Add_DoesNotThrow()
    {
        var act = () => CacheWeaveMeter.Evictions.Add(1);
        act.Should().NotThrow();
    }

    [Fact]
    public void Duration_Record_DoesNotThrow()
    {
        var act = () => CacheWeaveMeter.Duration.Record(42.5);
        act.Should().NotThrow();
    }

    [Fact]
    public void Hits_Add_WithTags_DoesNotThrow()
    {
        var act = () => CacheWeaveMeter.Hits.Add(1,
            new System.Diagnostics.TagList { { "cache.key", "test-key" } });
        act.Should().NotThrow();
    }
}
