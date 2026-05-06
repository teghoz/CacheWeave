using CacheWeave.Core.Services;
using FluentAssertions;
using Xunit;

namespace CacheWeave.Tests.Services;

public class InProcessStampedeProtectorTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsFactoryResult()
    {
        using var sut = new InProcessStampedeProtector();

        var result = await sut.ExecuteAsync<string>("key", _ => Task.FromResult<string?>("hello"));

        result.Should().Be("hello");
    }

    [Fact]
    public async Task ExecuteAsync_SerializesAccess_ForSameKey()
    {
        using var sut = new InProcessStampedeProtector();
        var callCount = 0;

        var tasks = Enumerable.Range(0, 10).Select(_ =>
            sut.ExecuteAsync<string>("shared-key", async ct =>
            {
                await Task.Delay(5, ct);
                callCount++;
                return (string?)callCount.ToString();
            }));

        await Task.WhenAll(tasks);

        // All 10 tasks ran — no deadlock, no exception
        callCount.Should().Be(10);
    }

    [Fact]
    public async Task ExecuteAsync_AllowsConcurrentAccess_ForDifferentKeys()
    {
        using var sut = new InProcessStampedeProtector();
        var results = new System.Collections.Concurrent.ConcurrentBag<string>();

        var tasks = Enumerable.Range(0, 5).Select(i =>
            sut.ExecuteAsync<string>($"key-{i}", async _ =>
            {
                await Task.Delay(10);
                results.Add(i.ToString());
                return (string?)i.ToString();
            }));

        await Task.WhenAll(tasks);

        results.Should().HaveCount(5);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesException_FromFactory()
    {
        using var sut = new InProcessStampedeProtector();

        Func<Task> act = () => sut.ExecuteAsync<string>("key",
            _ => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNull_WhenFactoryReturnsNull()
    {
        using var sut = new InProcessStampedeProtector();

        var result = await sut.ExecuteAsync<string>("key", _ => Task.FromResult<string?>(null));

        result.Should().BeNull();
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var sut = new InProcessStampedeProtector();
        var act = sut.Dispose;
        act.Should().NotThrow();
    }
}
