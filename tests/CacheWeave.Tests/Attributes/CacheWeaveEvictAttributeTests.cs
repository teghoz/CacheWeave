using CacheWeave.Core;
using FluentAssertions;
using Xunit;

namespace CacheWeave.Tests.Attributes;

public class CacheWeaveEvictAttributeTests
{
    [Fact]
    public void CanSetKey()
    {
        var attr = new CacheWeaveEvictAttribute { Key = "my-key" };
        attr.Key.Should().Be("my-key");
    }

    [Fact]
    public void CanSetPrefix()
    {
        var attr = new CacheWeaveEvictAttribute { Prefix = "products:" };
        attr.Prefix.Should().Be("products:");
    }

    [Fact]
    public void EvictOnFailure_DefaultsFalse()
    {
        var attr = new CacheWeaveEvictAttribute();
        attr.EvictOnFailure.Should().BeFalse();
    }

    [Fact]
    public void AllowMultiple_IsTrue()
    {
        var usageAttr = typeof(CacheWeaveEvictAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usageAttr.AllowMultiple.Should().BeTrue();
    }
}
