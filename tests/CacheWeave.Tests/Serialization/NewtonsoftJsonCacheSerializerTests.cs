using CacheWeave.Core.Serialization;
using FluentAssertions;
using Xunit;

namespace CacheWeave.Tests.Serialization;

public class NewtonsoftJsonCacheSerializerTests
{
    private readonly NewtonsoftJsonCacheSerializer _sut = new();

    private record Product(int Id, string Name);

    [Fact]
    public void Serialize_Generic_ProducesJson()
    {
        var json = _sut.Serialize(new Product(1, "Steel"));
        json.Should().Contain("Steel");
    }

    [Fact]
    public void Deserialize_Generic_ReturnsOriginalValue()
    {
        var original = new Product(42, "Aluminium");
        var json = _sut.Serialize(original);
        var result = _sut.Deserialize<Product>(json);
        result.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Serialize_NonGeneric_ProducesJson()
    {
        object value = new Product(7, "Copper");
        var json = _sut.Serialize(value, typeof(Product));
        json.Should().Contain("Copper");
    }

    [Fact]
    public void Deserialize_NonGeneric_ReturnsOriginalValue()
    {
        var original = new Product(7, "Copper");
        var json = _sut.Serialize(original, typeof(Product));
        var result = _sut.Deserialize(json, typeof(Product));
        result.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Deserialize_Generic_ReturnsNull_ForNullJson()
    {
        var result = _sut.Deserialize<Product>("null");
        result.Should().BeNull();
    }

    [Fact]
    public void Constructor_Throws_WhenSettingsIsNull()
    {
        var act = () => new NewtonsoftJsonCacheSerializer(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // -------------------------------------------------------------------------
    // Circular reference handling (Fix 1)
    // -------------------------------------------------------------------------

    private class Node
    {
        public string Name { get; set; } = "";
        public Node? Child { get; set; }
    }

    [Fact]
    public void Serialize_DoesNotThrow_ForCircularReference()
    {
        var parent = new Node { Name = "parent" };
        var child = new Node { Name = "child", Child = parent }; // circular
        parent.Child = child;

        // Should not throw — ReferenceLoopHandling.Ignore is set by default
        var act = () => _sut.Serialize(parent);
        act.Should().NotThrow();
    }

    [Fact]
    public void Serialize_CircularReference_ProducesValidJson()
    {
        var parent = new Node { Name = "parent" };
        parent.Child = new Node { Name = "child", Child = parent };

        var json = _sut.Serialize(parent);
        json.Should().Contain("parent");
        json.Should().Contain("child");
    }
}
