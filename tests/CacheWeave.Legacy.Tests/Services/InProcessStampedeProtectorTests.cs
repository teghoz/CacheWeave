using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CacheWeave.Legacy.Services;
using FluentAssertions;
using Xunit;

namespace CacheWeave.Legacy.Tests.Services
{
    public class InProcessStampedeProtectorTests
    {
        private readonly InProcessStampedeProtector _sut = new InProcessStampedeProtector();

        [Fact]
        public async Task ExecuteAsync_ReturnsFactoryResult()
        {
            var result = await _sut.ExecuteAsync<string>("k", _ => Task.FromResult<string>("value"));
            result.Should().Be("value");
        }

        [Fact]
        public async Task ExecuteAsync_ReturnsNull_WhenFactoryReturnsNull()
        {
            var result = await _sut.ExecuteAsync<string>("k", _ => Task.FromResult<string>(null!));
            result.Should().BeNull();
        }

        [Fact]
        public async Task ExecuteAsync_SerializesCallsForSameKey()
        {
            var callOrder = new List<int>();
            var tcs = new TaskCompletionSource<bool>();

            var t1 = _sut.ExecuteAsync<int>("k", async _ =>
            {
                callOrder.Add(1);
                await tcs.Task;
                return 1;
            });

            // Give t1 time to acquire the lock
            await Task.Delay(50);

            var t2 = _sut.ExecuteAsync<int>("k", _ =>
            {
                callOrder.Add(2);
                return Task.FromResult(2);
            });

            tcs.SetResult(true);
            await Task.WhenAll(t1, t2);

            // t1 must complete before t2 starts
            callOrder[0].Should().Be(1);
            callOrder[1].Should().Be(2);
        }

        [Fact]
        public async Task ExecuteAsync_AllowsConcurrentCallsForDifferentKeys()
        {
            var barrier = new SemaphoreSlim(0, 2);
            var reached = 0;

            var t1 = _sut.ExecuteAsync<int>("key1", async _ =>
            {
                Interlocked.Increment(ref reached);
                barrier.Release();
                await Task.Delay(50);
                return 1;
            });

            var t2 = _sut.ExecuteAsync<int>("key2", async _ =>
            {
                Interlocked.Increment(ref reached);
                barrier.Release();
                await Task.Delay(50);
                return 2;
            });

            // Both should reach the barrier concurrently
            var bothReached = await barrier.WaitAsync(TimeSpan.FromSeconds(2)) &&
                              await barrier.WaitAsync(TimeSpan.FromSeconds(2));

            bothReached.Should().BeTrue();
            await Task.WhenAll(t1, t2);
        }

        [Fact]
        public async Task ExecuteAsync_PropagatesException_FromFactory()
        {
            Func<Task> act = () => _sut.ExecuteAsync<string>("k", _ => throw new InvalidOperationException("boom"));
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        }
    }
}
