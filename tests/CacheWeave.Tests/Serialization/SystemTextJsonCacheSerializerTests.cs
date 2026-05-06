using CacheWeave.Core.Serialization;
using FluentAssertions;
using Xunit;

namespace CacheWeave.Tests.Serialization;

public class SystemTextJsonCacheSerializerTests
{
    private readonly SystemTextJsonCacheSerializer _sut = new();

    private record Product(int Id, string Name);

    [Fact]
    public void Serialize_Generic_ProducesJson()
    {
        var json = _sut.Serialize(new Product(1, "Steel"));
        // CamelCase policy lowercases property *names* (id, name), not values
        json.Should().Contain("Steel");
        json.Should().Contain("\"name\"");
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
        json.Should().Contain("\"name\"");
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
}
