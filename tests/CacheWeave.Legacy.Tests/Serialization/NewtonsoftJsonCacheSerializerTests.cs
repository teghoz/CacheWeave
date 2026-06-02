using System;
using CacheWeave.Legacy.Serialization;
using FluentAssertions;
using Xunit;

namespace CacheWeave.Legacy.Tests.Serialization
{
    public class NewtonsoftJsonCacheSerializerTests
    {
        private readonly NewtonsoftJsonCacheSerializer _sut = new NewtonsoftJsonCacheSerializer();

        private class Item
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        [Fact]
        public void Serialize_Generic_ThenDeserialize_ReturnsEquivalentObject()
        {
            var item = new Item { Id = 1, Name = "Widget" };
            var json = _sut.Serialize(item);
            var result = _sut.Deserialize<Item>(json);
            result.Should().BeEquivalentTo(item);
        }

        [Fact]
        public void Serialize_WithType_ThenDeserialize_ReturnsEquivalentObject()
        {
            var item = new Item { Id = 2, Name = "Gadget" };
            var json = _sut.Serialize(item, typeof(Item));
            var result = _sut.Deserialize(json, typeof(Item)) as Item;
            result.Should().BeEquivalentTo(item);
        }

        [Fact]
        public void Serialize_ProducesValidJson()
        {
            var json = _sut.Serialize(new Item { Id = 3, Name = "Thing" });
            // Newtonsoft preserves PascalCase by default
            json.Should().ContainAny("\"Id\"", "\"id\"");
        }

        [Fact]
        public void Deserialize_ReturnsDefault_ForNullJson()
        {
            var result = _sut.Deserialize<Item>("null");
            result.Should().BeNull();
        }

        [Fact]
        public void Serialize_String_RoundTrips()
        {
            var json = _sut.Serialize("hello");
            var result = _sut.Deserialize<string>(json);
            result.Should().Be("hello");
        }

        [Fact]
        public void Serialize_Int_RoundTrips()
        {
            var json = _sut.Serialize(42);
            var result = _sut.Deserialize<int>(json);
            result.Should().Be(42);
        }
    }
}
