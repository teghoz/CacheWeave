using CacheWeave.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CacheWeave.Tests.Configuration;

public class CacheWeaveOptionsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var opts = new CacheWeaveOptions();

        opts.Enabled.Should().BeTrue();
        opts.KeySeparator.Should().Be(":");
        opts.KeyVersion.Should().BeNull();
        opts.DefaultExpiry.Should().Be(TimeSpan.FromSeconds(300));
        opts.DefaultNoCacheCondition.Should().Be(NoCacheCondition.OnErrorOrEmpty);
        opts.Serializer.Should().Be(CacheWeaveSerializerType.SystemTextJson);
        opts.EnableCompression.Should().BeFalse();
        opts.EnableMetrics.Should().BeTrue();
        opts.DiagnosticLogLevel.Should().Be(LogLevel.Debug);
    }

    [Fact]
    public void Properties_CanBeOverridden()
    {
        var opts = new CacheWeaveOptions
        {
            KeySeparator = ".",
            KeyVersion = "v2",
            DefaultExpiry = TimeSpan.FromMinutes(10),
            Serializer = CacheWeaveSerializerType.NewtonsoftJson,
            EnableCompression = true,
            EnableMetrics = false,
            DiagnosticLogLevel = LogLevel.Information
        };

        opts.KeySeparator.Should().Be(".");
        opts.KeyVersion.Should().Be("v2");
        opts.DefaultExpiry.Should().Be(TimeSpan.FromMinutes(10));
        opts.Serializer.Should().Be(CacheWeaveSerializerType.NewtonsoftJson);
        opts.EnableCompression.Should().BeTrue();
        opts.EnableMetrics.Should().BeFalse();
        opts.DiagnosticLogLevel.Should().Be(LogLevel.Information);
    }
}
