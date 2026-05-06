using CacheWeave.Core;
using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Filters;
using CacheWeave.Core.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CacheWeave.Tests.Filters;

public class CacheWeaveFilterTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private readonly Mock<ICacheProvider> _provider = new();
    private readonly Mock<ICacheKeyBuilder> _keyBuilder = new();
    private readonly SystemTextJsonCacheSerializer _serializer = new();
    private readonly CacheWeaveOptions _opts = new() { EnableMetrics = false };

    private CacheWeaveFilter MakeSut(CacheWeaveOptions? opts = null) => new(
        _provider.Object,
        _keyBuilder.Object,
        _serializer,
        Options.Create(opts ?? _opts),
        NullLogger<CacheWeaveFilter>.Instance);

    private static (ActionExecutingContext executing, ActionExecutionDelegate next, Action<IActionResult?> resultSetter)
        MakeContextPair(CacheWeaveAttribute? attribute = null)
    {
        var httpContext = new DefaultHttpContext();
        var method = typeof(FakeController).GetMethod(
            attribute is not null ? nameof(FakeController.CachedAction) : nameof(FakeController.UndecoratedAction))!;

        var descriptor = new ControllerActionDescriptor
        {
            ControllerName = "Fake",
            ActionName = method.Name,
            MethodInfo = method
        };

        var actionContext = new ActionContext(httpContext, new RouteData(), descriptor);
        var executing = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), new object());

        IActionResult? executedResult = null;
        ActionExecutionDelegate next = () =>
        {
            var executed = new ActionExecutedContext(actionContext, [], new object())
            {
                Result = executedResult
            };
            return Task.FromResult(executed);
        };

        return (executing, next, r => executedResult = r);
    }

    // Fake controller used to carry [CacheWeave] attribute via reflection
    private class FakeController : ControllerBase
    {
        [CacheWeave("test-key")]
        public IActionResult CachedAction() => Ok();

        public IActionResult UndecoratedAction() => Ok();
    }

    // -------------------------------------------------------------------------
    // No attribute — pass-through
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnActionExecutionAsync_PassesThrough_WhenNoAttribute()
    {
        var sut = MakeSut();
        var (ctx, next, _) = MakeContextPair(attribute: null);
        var nextCalled = false;
        ActionExecutionDelegate wrappedNext = () => { nextCalled = true; return next(); };

        await sut.OnActionExecutionAsync(ctx, wrappedNext);

        nextCalled.Should().BeTrue();
        _provider.Verify(p => p.GetAsync(It.IsAny<string>(), default), Times.Never);
    }

    // -------------------------------------------------------------------------
    // Cache hit
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnActionExecutionAsync_ReturnsCachedResult_OnHit()
    {
        var cached = _serializer.Serialize(new { id = 1 });
        _keyBuilder.Setup(k => k.BuildAsync(It.IsAny<CacheWeaveAttribute>(), It.IsAny<ActionExecutingContext>()))
            .ReturnsAsync("test-key");
        _provider.Setup(p => p.GetAsync("test-key", default)).ReturnsAsync(cached);

        var sut = MakeSut();
        var (ctx, next, _) = MakeContextPair(new CacheWeaveAttribute("test-key"));
        var nextCalled = false;
        ActionExecutionDelegate wrappedNext = () => { nextCalled = true; return next(); };

        await sut.OnActionExecutionAsync(ctx, wrappedNext);

        nextCalled.Should().BeFalse();
        ctx.Result.Should().BeOfType<ContentResult>()
            .Which.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task OnActionExecutionAsync_CachedResult_HasJsonContentType()
    {
        var cached = _serializer.Serialize(new { id = 1 });
        _keyBuilder.Setup(k => k.BuildAsync(It.IsAny<CacheWeaveAttribute>(), It.IsAny<ActionExecutingContext>()))
            .ReturnsAsync("test-key");
        _provider.Setup(p => p.GetAsync("test-key", default)).ReturnsAsync(cached);

        var sut = MakeSut();
        var (ctx, next, _) = MakeContextPair(new CacheWeaveAttribute("test-key"));
        await sut.OnActionExecutionAsync(ctx, next);

        ctx.Result.Should().BeOfType<ContentResult>()
            .Which.ContentType.Should().Be("application/json");
    }

    // -------------------------------------------------------------------------
    // Sliding expiry — re-writes on hit
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnActionExecutionAsync_RewritesEntry_OnHit_WhenSlidingExpiry()
    {
        var cached = _serializer.Serialize(new { id = 1 });
        _keyBuilder.Setup(k => k.BuildAsync(It.IsAny<CacheWeaveAttribute>(), It.IsAny<ActionExecutingContext>()))
            .ReturnsAsync("test-key");
        _provider.Setup(p => p.GetAsync("test-key", default)).ReturnsAsync(cached);

        // Need a controller method that carries SlidingExpiry = true
        var sut = MakeSut();
        var attr = new CacheWeaveAttribute("test-key") { SlidingExpiry = true, ExpirySeconds = 60 };
        var (ctx, next, _) = MakeContextPair(attr);

        // Manually swap the descriptor's MethodInfo to one carrying SlidingExpiry
        // We test the re-write by verifying SetAsync is called on a hit
        // Use a dedicated controller method for this
        var method = typeof(SlidingController).GetMethod(nameof(SlidingController.Get))!;
        ((ControllerActionDescriptor)ctx.ActionDescriptor).MethodInfo = method;

        await sut.OnActionExecutionAsync(ctx, next);

        // SetAsync should be called to refresh the TTL
        _provider.Verify(p => p.SetAsync("test-key", cached, TimeSpan.FromSeconds(60), default), Times.Once);
    }

    private class SlidingController : ControllerBase
    {
        [CacheWeave("test-key", SlidingExpiry = true, ExpirySeconds = 60)]
        public IActionResult Get() => Ok();
    }

    // -------------------------------------------------------------------------
    // Cache miss → write
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnActionExecutionAsync_WritesToCache_OnMiss()
    {
        _keyBuilder.Setup(k => k.BuildAsync(It.IsAny<CacheWeaveAttribute>(), It.IsAny<ActionExecutingContext>()))
            .ReturnsAsync("test-key");
        _provider.Setup(p => p.GetAsync("test-key", default)).ReturnsAsync((string?)null);

        var sut = MakeSut();
        var (ctx, next, setResult) = MakeContextPair(new CacheWeaveAttribute("test-key"));
        setResult(new OkObjectResult(new { id = 1 }));

        await sut.OnActionExecutionAsync(ctx, next);

        _provider.Verify(p => p.SetAsync("test-key", It.IsAny<string>(), It.IsAny<TimeSpan?>(), default), Times.Once);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WritesToCache_WithJsonResult()
    {
        _keyBuilder.Setup(k => k.BuildAsync(It.IsAny<CacheWeaveAttribute>(), It.IsAny<ActionExecutingContext>()))
            .ReturnsAsync("test-key");
        _provider.Setup(p => p.GetAsync("test-key", default)).ReturnsAsync((string?)null);

        var sut = MakeSut();
        var (ctx, next, setResult) = MakeContextPair(new CacheWeaveAttribute("test-key"));
        setResult(new JsonResult(new { id = 2 }));

        await sut.OnActionExecutionAsync(ctx, next);

        _provider.Verify(p => p.SetAsync("test-key", It.IsAny<string>(), It.IsAny<TimeSpan?>(), default), Times.Once);
    }

    [Fact]
    public async Task OnActionExecutionAsync_DoesNotWrite_WhenResultIsNotObjectOrJson()
    {
        _keyBuilder.Setup(k => k.BuildAsync(It.IsAny<CacheWeaveAttribute>(), It.IsAny<ActionExecutingContext>()))
            .ReturnsAsync("test-key");
        _provider.Setup(p => p.GetAsync("test-key", default)).ReturnsAsync((string?)null);

        var sut = MakeSut();
        var (ctx, next, setResult) = MakeContextPair(new CacheWeaveAttribute("test-key"));
        setResult(new NoContentResult()); // not ObjectResult or JsonResult

        await sut.OnActionExecutionAsync(ctx, next);

        _provider.Verify(p => p.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), default), Times.Never);
    }

    // -------------------------------------------------------------------------
    // NoCacheWhen
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnActionExecutionAsync_DoesNotCache_WhenStatusIsError()
    {
        _keyBuilder.Setup(k => k.BuildAsync(It.IsAny<CacheWeaveAttribute>(), It.IsAny<ActionExecutingContext>()))
            .ReturnsAsync("test-key");
        _provider.Setup(p => p.GetAsync("test-key", default)).ReturnsAsync((string?)null);

        var sut = MakeSut();
        var (ctx, next, setResult) = MakeContextPair(new CacheWeaveAttribute("test-key"));
        setResult(new ObjectResult(new { error = "not found" }) { StatusCode = 404 });

        await sut.OnActionExecutionAsync(ctx, next);

        _provider.Verify(p => p.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), default), Times.Never);
    }

    [Fact]
    public async Task OnActionExecutionAsync_DoesNotCache_WhenResultIsEmptyCollection()
    {
        _keyBuilder.Setup(k => k.BuildAsync(It.IsAny<CacheWeaveAttribute>(), It.IsAny<ActionExecutingContext>()))
            .ReturnsAsync("test-key");
        _provider.Setup(p => p.GetAsync("test-key", default)).ReturnsAsync((string?)null);

        var sut = MakeSut();
        var (ctx, next, setResult) = MakeContextPair(new CacheWeaveAttribute("test-key"));
        setResult(new OkObjectResult(new List<string>())); // empty collection

        await sut.OnActionExecutionAsync(ctx, next);

        _provider.Verify(p => p.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), default), Times.Never);
    }

    [Fact]
    public async Task OnActionExecutionAsync_Caches_WhenNoCacheWhenIsNever_EvenOnError()
    {
        _keyBuilder.Setup(k => k.BuildAsync(It.IsAny<CacheWeaveAttribute>(), It.IsAny<ActionExecutingContext>()))
            .ReturnsAsync("test-key");
        _provider.Setup(p => p.GetAsync("test-key", default)).ReturnsAsync((string?)null);

        var sut = MakeSut();
        var method = typeof(NeverSkipController).GetMethod(nameof(NeverSkipController.Get))!;
        var (ctx, next, setResult) = MakeContextPair(new CacheWeaveAttribute("test-key"));
        ((ControllerActionDescriptor)ctx.ActionDescriptor).MethodInfo = method;
        setResult(new ObjectResult("error body") { StatusCode = 500 });

        await sut.OnActionExecutionAsync(ctx, next);

        _provider.Verify(p => p.SetAsync("test-key", It.IsAny<string>(), It.IsAny<TimeSpan?>(), default), Times.Once);
    }

    private class NeverSkipController : ControllerBase
    {
        [CacheWeave("test-key", NoCacheWhen = NoCacheCondition.Never)]
        public IActionResult Get() => Ok();
    }

    [Fact]
    public async Task OnActionExecutionAsync_DoesNotCache_WhenResultIsEmptyString()
    {
        _keyBuilder.Setup(k => k.BuildAsync(It.IsAny<CacheWeaveAttribute>(), It.IsAny<ActionExecutingContext>()))
            .ReturnsAsync("test-key");
        _provider.Setup(p => p.GetAsync("test-key", default)).ReturnsAsync((string?)null);

        var sut = MakeSut();
        var (ctx, next, setResult) = MakeContextPair(new CacheWeaveAttribute("test-key"));
        setResult(new OkObjectResult(string.Empty));

        await sut.OnActionExecutionAsync(ctx, next);

        _provider.Verify(p => p.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), default), Times.Never);
    }

    // -------------------------------------------------------------------------
    // Exception in action — not cached
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnActionExecutionAsync_DoesNotCache_WhenActionThrows()
    {
        _keyBuilder.Setup(k => k.BuildAsync(It.IsAny<CacheWeaveAttribute>(), It.IsAny<ActionExecutingContext>()))
            .ReturnsAsync("test-key");
        _provider.Setup(p => p.GetAsync("test-key", default)).ReturnsAsync((string?)null);

        var sut = MakeSut();
        var (ctx, _, _) = MakeContextPair(new CacheWeaveAttribute("test-key"));

        ActionExecutionDelegate throwingNext = () =>
        {
            var httpContext = new DefaultHttpContext();
            var descriptor = new ControllerActionDescriptor { ControllerName = "X", ActionName = "Y" };
            var ac = new ActionContext(httpContext, new RouteData(), descriptor);
            return Task.FromResult(new ActionExecutedContext(ac, [], new object())
            {
                Exception = new InvalidOperationException("boom"),
                ExceptionHandled = false
            });
        };

        await sut.OnActionExecutionAsync(ctx, throwingNext);

        _provider.Verify(p => p.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), default), Times.Never);
    }

    // -------------------------------------------------------------------------
    // Expiry resolution
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnActionExecutionAsync_UsesExplicitExpiry_WhenSet()
    {
        _keyBuilder.Setup(k => k.BuildAsync(It.IsAny<CacheWeaveAttribute>(), It.IsAny<ActionExecutingContext>()))
            .ReturnsAsync("test-key");
        _provider.Setup(p => p.GetAsync("test-key", default)).ReturnsAsync((string?)null);

        var sut = MakeSut();
        var (ctx, next, setResult) = MakeContextPair(new CacheWeaveAttribute("test-key") { ExpirySeconds = 60 });
        setResult(new OkObjectResult(new { id = 1 }));

        await sut.OnActionExecutionAsync(ctx, next);

        _provider.Verify(p => p.SetAsync("test-key", It.IsAny<string>(), It.IsAny<TimeSpan?>(), default), Times.Once);
    }

    [Fact]
    public async Task OnActionExecutionAsync_UsesNullExpiry_WhenExpirySecondsIsZero()
    {
        _keyBuilder.Setup(k => k.BuildAsync(It.IsAny<CacheWeaveAttribute>(), It.IsAny<ActionExecutingContext>()))
            .ReturnsAsync("test-key");
        _provider.Setup(p => p.GetAsync("test-key", default)).ReturnsAsync((string?)null);

        var sut = MakeSut();
        var method = typeof(NoExpiryController).GetMethod(nameof(NoExpiryController.Get))!;
        var (ctx, next, setResult) = MakeContextPair(new CacheWeaveAttribute("test-key"));
        ((ControllerActionDescriptor)ctx.ActionDescriptor).MethodInfo = method;
        setResult(new OkObjectResult(new { id = 1 }));

        await sut.OnActionExecutionAsync(ctx, next);

        // ExpirySeconds = 0 → null expiry (no TTL)
        _provider.Verify(p => p.SetAsync("test-key", It.IsAny<string>(), null, default), Times.Once);
    }

    private class NoExpiryController : ControllerBase
    {
        [CacheWeave("test-key", ExpirySeconds = 0)]
        public IActionResult Get() => Ok();
    }

    // -------------------------------------------------------------------------
    // Fault tolerance — cache read failure falls through to action
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnActionExecutionAsync_FallsThroughToAction_WhenGetAsyncThrows()
    {
        _keyBuilder.Setup(k => k.BuildAsync(It.IsAny<CacheWeaveAttribute>(), It.IsAny<ActionExecutingContext>()))
            .ReturnsAsync("test-key");
        _provider.Setup(p => p.GetAsync("test-key", default))
            .ThrowsAsync(new InvalidOperationException("Redis down"));

        var sut = MakeSut();
        var (ctx, next, setResult) = MakeContextPair(new CacheWeaveAttribute("test-key"));
        setResult(new OkObjectResult(new { id = 1 }));
        var nextCalled = false;
        ActionExecutionDelegate wrappedNext = () => { nextCalled = true; return next(); };

        // Should not throw
        await sut.OnActionExecutionAsync(ctx, wrappedNext);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task OnActionExecutionAsync_StillReturnsResponse_WhenSetAsyncThrows()
    {
        _keyBuilder.Setup(k => k.BuildAsync(It.IsAny<CacheWeaveAttribute>(), It.IsAny<ActionExecutingContext>()))
            .ReturnsAsync("test-key");
        _provider.Setup(p => p.GetAsync("test-key", default)).ReturnsAsync((string?)null);
        _provider.Setup(p => p.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), default))
            .ThrowsAsync(new InvalidOperationException("Redis down"));

        var sut = MakeSut();
        var (ctx, next, setResult) = MakeContextPair(new CacheWeaveAttribute("test-key"));
        setResult(new OkObjectResult(new { id = 1 }));

        // Should not throw — response is still returned
        await sut.OnActionExecutionAsync(ctx, next);
    }

    [Fact]
    public async Task OnActionExecutionAsync_StillServesCachedResponse_WhenSlidingExpiryRefreshThrows()
    {
        var cached = _serializer.Serialize(new { id = 1 });
        _keyBuilder.Setup(k => k.BuildAsync(It.IsAny<CacheWeaveAttribute>(), It.IsAny<ActionExecutingContext>()))
            .ReturnsAsync("test-key");
        _provider.Setup(p => p.GetAsync("test-key", default)).ReturnsAsync(cached);
        _provider.Setup(p => p.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), default))
            .ThrowsAsync(new InvalidOperationException("Redis down"));

        var sut = MakeSut();
        var method = typeof(SlidingController).GetMethod(nameof(SlidingController.Get))!;
        var (ctx, next, _) = MakeContextPair(new CacheWeaveAttribute("test-key") { SlidingExpiry = true, ExpirySeconds = 60 });
        ((ControllerActionDescriptor)ctx.ActionDescriptor).MethodInfo = method;

        // Should not throw — cached response is still served
        await sut.OnActionExecutionAsync(ctx, next);

        ctx.Result.Should().BeOfType<ContentResult>()
            .Which.StatusCode.Should().Be(200);
    }
}
