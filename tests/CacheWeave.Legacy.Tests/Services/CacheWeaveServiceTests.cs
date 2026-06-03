using System;
using System.Threading;
using System.Threading.Tasks;
using CacheWeave.Legacy.Abstractions;
using CacheWeave.Legacy.Serialization;
using CacheWeave.Legacy.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CacheWeave.Legacy.Tests.Services
{
    public class CacheWeaveServiceTests
    {
        private readonly Mock<ICacheProvider> _provider = new Mock<ICacheProvider>();
        private readonly SystemTextJsonCacheSerializer _serializer = new SystemTextJsonCacheSerializer();
        private readonly InProcessStampedeProtector _stampede = new InProcessStampedeProtector();
        private readonly CacheWeaveOptions _opts = new CacheWeaveOptions { DefaultExpiry = TimeSpan.FromMinutes(5) };

        private CacheWeaveService MakeSut() => new CacheWeaveService(
            _provider.Object,
            _serializer,
            _stampede,
            Options.Create(_opts));

        private class Item
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        // -------------------------------------------------------------------------
        // GetOrSetAsync
        // -------------------------------------------------------------------------

        [Fact]
        public async Task GetOrSetAsync_ReturnsCachedValue_OnHit()
        {
            var item = new Item { Id = 1, Name = "Steel" };
            _provider.Setup(p => p.GetAsync("k", default))
                     .ReturnsAsync(_serializer.Serialize(item));

            var sut = MakeSut();
            var result = await sut.GetOrSetAsync<Item>("k", _ => Task.FromResult<Item>(new Item { Id = 99, Name = "Other" }));

            result.Should().BeEquivalentTo(item);
            _provider.Verify(p => p.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), default), Times.Never);
        }

        [Fact]
        public async Task GetOrSetAsync_InvokesFactory_OnMiss_AndStores()
        {
            _provider.Setup(p => p.GetAsync("k", default)).ReturnsAsync((string?)null);

            var sut = MakeSut();
            var result = await sut.GetOrSetAsync<Item>("k", _ => Task.FromResult<Item>(new Item { Id = 1, Name = "Steel" }));

            result.Should().BeEquivalentTo(new Item { Id = 1, Name = "Steel" });
            _provider.Verify(p => p.SetAsync("k", It.IsAny<string>(), _opts.DefaultExpiry, default), Times.Once);
        }

        [Fact]
        public async Task GetOrSetAsync_UsesExplicitExpiry_WhenProvided()
        {
            _provider.Setup(p => p.GetAsync("k", default)).ReturnsAsync((string?)null);
            var sut = MakeSut();

            await sut.GetOrSetAsync<Item>("k", _ => Task.FromResult<Item>(new Item { Id = 1, Name = "X" }), TimeSpan.FromSeconds(30));

            _provider.Verify(p => p.SetAsync("k", It.IsAny<string>(), TimeSpan.FromSeconds(30), default), Times.Once);
        }

        [Fact]
        public async Task GetOrSetAsync_DoesNotStore_WhenFactoryReturnsNull()
        {
            _provider.Setup(p => p.GetAsync("k", default)).ReturnsAsync((string?)null);
            var sut = MakeSut();

            var result = await sut.GetOrSetAsync<Item>("k", _ => Task.FromResult<Item>(null!));

            result.Should().BeNull();
            _provider.Verify(p => p.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), default), Times.Never);
        }

        // -------------------------------------------------------------------------
        // GetAsync
        // -------------------------------------------------------------------------

        [Fact]
        public async Task GetAsync_ReturnsDeserializedValue_OnHit()
        {
            var item = new Item { Id = 5, Name = "Copper" };
            _provider.Setup(p => p.GetAsync("k", default)).ReturnsAsync(_serializer.Serialize(item));
            var sut = MakeSut();

            var result = await sut.GetAsync<Item>("k");

            result.Should().BeEquivalentTo(item);
        }

        [Fact]
        public async Task GetAsync_ReturnsDefault_OnMiss()
        {
            _provider.Setup(p => p.GetAsync("k", default)).ReturnsAsync((string?)null);
            var sut = MakeSut();

            var result = await sut.GetAsync<Item>("k");

            result.Should().BeNull();
        }

        // -------------------------------------------------------------------------
        // SetAsync
        // -------------------------------------------------------------------------

        [Fact]
        public async Task SetAsync_SerializesAndStores()
        {
            var sut = MakeSut();
            var item = new Item { Id = 3, Name = "Iron" };

            await sut.SetAsync("k", item);

            _provider.Verify(p => p.SetAsync("k", It.IsAny<string>(), _opts.DefaultExpiry, default), Times.Once);
        }

        [Fact]
        public async Task SetAsync_UsesExplicitExpiry()
        {
            var sut = MakeSut();

            await sut.SetAsync("k", new Item { Id = 1, Name = "X" }, TimeSpan.FromSeconds(10));

            _provider.Verify(p => p.SetAsync("k", It.IsAny<string>(), TimeSpan.FromSeconds(10), default), Times.Once);
        }

        // -------------------------------------------------------------------------
        // InvalidateAsync / InvalidateByPrefixAsync
        // -------------------------------------------------------------------------

        [Fact]
        public async Task InvalidateAsync_CallsRemove()
        {
            var sut = MakeSut();
            await sut.InvalidateAsync("k");
            _provider.Verify(p => p.RemoveAsync("k", default), Times.Once);
        }

        [Fact]
        public async Task InvalidateByPrefixAsync_CallsRemoveByPrefix()
        {
            var sut = MakeSut();
            await sut.InvalidateByPrefixAsync("prefix:");
            _provider.Verify(p => p.RemoveByPrefixAsync("prefix:", default), Times.Once);
        }

        [Fact]
        public async Task InvalidateByPrefixesAsync_CallsRemoveByPrefix_ForEachPrefix()
        {
            var sut = MakeSut();
            await sut.InvalidateByPrefixesAsync(new[] { "products:", "dashboard:", "stats:" });

            _provider.Verify(p => p.RemoveByPrefixAsync("products:", default), Times.Once);
            _provider.Verify(p => p.RemoveByPrefixAsync("dashboard:", default), Times.Once);
            _provider.Verify(p => p.RemoveByPrefixAsync("stats:", default), Times.Once);
        }

        [Fact]
        public async Task InvalidateByPrefixesAsync_EmptyList_DoesNotCallProvider()
        {
            var sut = MakeSut();
            await sut.InvalidateByPrefixesAsync(new string[0]);
            _provider.Verify(p => p.RemoveByPrefixAsync(It.IsAny<string>(), default), Times.Never);
        }

        // -------------------------------------------------------------------------
        // GlobalKeyPrefix
        // -------------------------------------------------------------------------

        [Fact]
        public async Task SetAsync_PrependGlobalKeyPrefix_WhenConfigured()
        {
            _opts.GlobalKeyPrefix = "myapp";
            var sut = MakeSut();

            await sut.SetAsync("products", "value");

            _provider.Verify(p => p.SetAsync("myapp:products", It.IsAny<string>(), It.IsAny<TimeSpan?>(), default), Times.Once);
        }

        [Fact]
        public async Task SetAsync_DoesNotDoublePrefix_WhenKeyAlreadyPrefixed()
        {
            _opts.GlobalKeyPrefix = "myapp";
            var sut = MakeSut();

            await sut.SetAsync("myapp:products", "value");

            _provider.Verify(p => p.SetAsync("myapp:products", It.IsAny<string>(), It.IsAny<TimeSpan?>(), default), Times.Once);
        }
    }
}
