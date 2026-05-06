using CacheWeave.Core;
using FluentAssertions;
using Xunit;

namespace CacheWeave.Tests.Attributes;

public class CacheWeaveAttributeTests
{
    [Fact]
    public void Constructor_WithKey_SetsKey()
    {
        var attr = new CacheWeaveAttribute("my-key");
        attr.Key.Should().Be("my-key");
    }

    [Fact]
    public void Constructor_Parameterless_SetsKeyToNull()
    {
        var attr = new CacheWeaveAttribute();
        attr.Key.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithKey_Throws_WhenKeyIsEmpty()
    {
        var act = () => new CacheWeaveAttribute("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithKey_Throws_WhenKeyIsWhitespace()
    {
        var act = () => new CacheWeaveAttribute("   ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Defaults_AreCorrect()
    {
        var attr = new CacheWeaveAttribute("k");
        attr.IncludeQueryParams.Should().BeTrue();
        attr.ExcludeParams.Should().BeEmpty();
        attr.HashBody.Should().BeFalse();
        attr.HashBodyFields.Should().BeEmpty();
        attr.ExpirySeconds.Should().Be(-1);
        attr.SlidingExpiry.Should().BeFalse();
        attr.NoCacheWhen.Should().Be(NoCacheCondition.OnErrorOrEmpty);
    }
}
