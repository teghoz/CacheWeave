using System;
using System.Threading.Tasks;
using CacheWeave.Legacy.Providers;
using FluentAssertions;
using Xunit;

namespace CacheWeave.Legacy.Tests.Providers
{
    public class DisabledCacheProviderTests
    {
        private readonly DisabledCacheProvider _sut = DisabledCacheProvider.Instance;

        [Fact]
        public async Task GetAsync_AlwaysReturnsNull()
        {
            var result = await _sut.GetAsync("any-key");
            result.Should().BeNull();
        }

        [Fact]
        public async Task SetAsync_DoesNotThrow()
        {
            Func<Task> act = () => _sut.SetAsync("k", "v", TimeSpan.FromMinutes(1));
            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task RemoveAsync_DoesNotThrow()
        {
            Func<Task> act = () => _sut.RemoveAsync("k");
            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task RemoveByPrefixAsync_DoesNotThrow()
        {
            Func<Task> act = () => _sut.RemoveByPrefixAsync("prefix:");
            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task SetThenGet_ReturnsNull_BecauseCachingIsDisabled()
        {
            await _sut.SetAsync("k", "v");
            var result = await _sut.GetAsync("k");
            result.Should().BeNull();
        }

        [Fact]
        public void Instance_IsSingleton()
        {
            DisabledCacheProvider.Instance.Should().BeSameAs(DisabledCacheProvider.Instance);
        }
    }
}
