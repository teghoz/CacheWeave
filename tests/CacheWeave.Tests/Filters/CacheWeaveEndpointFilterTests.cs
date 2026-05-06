using CacheWeave.Core;
using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Filters;
using CacheWeave.Core.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CacheWeave.Tests.Filters;

public class CacheWeaveEndpointFilterTests
{
    private readonly Mock<ICacheProvider> _provider = new();
    private readonly SystemTextJsonCacheSerializer _serializer = new();
    private readonly CacheWeaveOptions _opts = new();

    private CacheWeaveEndpointFilter MakeSut(CacheWeaveAttribute? attr = null) => new(
        attr ?? new CacheWeaveAttribute("ep-key"),
        _provider.Object,
        _serializer,
        Options.Create(_opts),
        NullLogger<CacheWeaveEndpointFilter>.Instance);

    private static EndpointFilterInvocationContext MakeContext(
        string? queryKey = null,
        string? queryValue = null)
    {
        var httpContext = new DefaultHttpContext();
        if (queryKey is not null)
            httpContext.Request.QueryString = new QueryString($"?{queryKey}={queryValue}");

        return new FakeEndpointFilterInvocationContext(httpContext);
    }

    private sealed class FakeEndpointFilterInvocationContext : EndpointFilterInvocationContext
    {
        public FakeEndpointFilterInvocationContext(HttpContext httpContext)
        {
            HttpContext = httpContext;
        }

        public override HttpContext HttpContext { get; }
        public override IList<object?> Arguments => [];
        public override T GetArgument<T>(int index) => throw new NotImplementedException();
    }

    // -------------------------------------------------------------------------
    // Cache hit
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_ReturnsCachedResult_OnHit()
    {
        var cached = _serializer.Serialize(new { id = 1 });
        _provider.Setup(p => p.GetAsync("ep-key", It.IsAny<CancellationToken>())).ReturnsAsync(cached);

        var sut = MakeSut();
        var ctx = MakeContext();
        var nextCalled = false;
        EndpointFilterDelegate next = _ => { nextCalled = true; return ValueTask.FromResult<object?>(null); };

        var result = await sut.InvokeAsync(ctx, next);

        nextCalled.Should().BeFalse();
        result.Should().NotBeNull();
    }

    // -------------------------------------------------------------------------
    // Sliding expiry on hit
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_RefreshesTtl_OnHit_WhenSlidingExpiry()
    {
        var cached = _serializer.Serialize(new { id = 1 });
        _provider.Setup(p => p.GetAsync("ep-key", It.IsAny<CancellationToken>())).ReturnsAsync(cached);

        var attr = new CacheWeaveAttribute("ep-key") { SlidingExpiry = true, ExpirySeconds = 30 };
        var sut = MakeSut(attr);
        var ctx = MakeContext();

        await sut.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null));

        _provider.Verify(p => p.SetAsync("ep-key", cached, TimeSpan.FromSeconds(30), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_DoesNotRefreshTtl_OnHit_WhenSlidingExpiryAndExpiryIsZero()
    {
        var cached = _serializer.Serialize(new { id = 1 });
        _provider.Setup(p => p.GetAsync("ep-key", It.IsAny<CancellationToken>())).ReturnsAsync(cached);

        var attr = new CacheWeaveAttribute("ep-key") { SlidingExpiry = true, ExpirySeconds = 0 };
        var sut = MakeSut(attr);
        var ctx = MakeContext();

        await sut.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null));

        _provider.Verify(p => p.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // Cache miss → write
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_WritesToCache_OnMiss()
    {
        _provider.Setup(p => p.GetAsync("ep-key", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var sut = MakeSut();
        var ctx = MakeContext();
        var handlerResult = new { id = 1 };

        var result = await sut.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(handlerResult));

        _provider.Verify(p => p.SetAsync("ep-key", It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Once);
        result.Should().Be(handlerResult);
    }

    [Fact]
    public async Task InvokeAsync_DoesNotWrite_WhenHandlerReturnsNull()
    {
        _provider.Setup(p => p.GetAsync("ep-key", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var sut = MakeSut();
        var ctx = MakeContext();

        await sut.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(null));

        _provider.Verify(p => p.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // Query param key building
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_AppendsQueryParams_ToKey()
    {
        _provider.Setup(p => p.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var attr = new CacheWeaveAttribute("ep-key") { IncludeQueryParams = true };
        var sut = MakeSut(attr);
        var ctx = MakeContext(queryKey: "page", queryValue: "2");

        await sut.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(new { items = 1 }));

        _provider.Verify(p => p.GetAsync("ep-key:page=2", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_ExcludesSpecifiedQueryParams()
    {
        _provider.Setup(p => p.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var attr = new CacheWeaveAttribute("ep-key")
        {
            IncludeQueryParams = true,
            ExcludeParams = ["token"]
        };
        var sut = MakeSut(attr);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?id=5&token=abc");
        var ctx = new FakeEndpointFilterInvocationContext(httpContext);

        await sut.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(new { id = 5 }));

        _provider.Verify(p => p.GetAsync("ep-key:id=5", It.IsAny<CancellationToken>()), Times.Once);
    }

    // -------------------------------------------------------------------------
    // KeyVersion appended when set
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_AppendsKeyVersion_WhenSet()
    {
        var opts = new CacheWeaveOptions { KeyVersion = "v3" };
        var sut = new CacheWeaveEndpointFilter(
            new CacheWeaveAttribute("ep-key"),
            _provider.Object,
            _serializer,
            Options.Create(opts),
            NullLogger<CacheWeaveEndpointFilter>.Instance);

        _provider.Setup(p => p.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var ctx = MakeContext();
        await sut.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(new { x = 1 }));

        _provider.Verify(p => p.GetAsync("ep-key:v3", It.IsAny<CancellationToken>()), Times.Once);
    }

    // -------------------------------------------------------------------------
    // Expiry resolution
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_UsesNullExpiry_WhenExpirySecondsIsZero()
    {
        _provider.Setup(p => p.GetAsync("ep-key", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var attr = new CacheWeaveAttribute("ep-key") { ExpirySeconds = 0 };
        var sut = MakeSut(attr);
        var ctx = MakeContext();

        await sut.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(new { id = 1 }));

        _provider.Verify(p => p.SetAsync("ep-key", It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_UsesExplicitExpiry_WhenExpirySecondsIsPositive()
    {
        _provider.Setup(p => p.GetAsync("ep-key", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var attr = new CacheWeaveAttribute("ep-key") { ExpirySeconds = 120 };
        var sut = MakeSut(attr);
        var ctx = MakeContext();

        await sut.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(new { id = 1 }));

        _provider.Verify(p => p.SetAsync("ep-key", It.IsAny<string>(), TimeSpan.FromSeconds(120), It.IsAny<CancellationToken>()), Times.Once);
    }
}
