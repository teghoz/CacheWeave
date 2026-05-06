using System.Reflection;
using CacheWeave.Core;
using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Filters;
using CacheWeave.Core.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CacheWeave.Tests.Filters;

public class CacheWeavePageFilterTests
{
    private readonly Mock<ICacheProvider> _provider = new();
    private readonly SystemTextJsonCacheSerializer _serializer = new();
    private readonly CacheWeaveOptions _opts = new();

    private CacheWeavePageFilter MakeSut(CacheWeaveOptions? opts = null) => new(
        _provider.Object,
        _serializer,
        Options.Create(opts ?? _opts),
        NullLogger<CacheWeavePageFilter>.Instance);

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static PageContext MakePageContext(
        string? queryKey = null,
        string? queryValue = null)
    {
        var httpContext = new DefaultHttpContext();
        if (queryKey is not null)
            httpContext.Request.QueryString = new QueryString($"?{queryKey}={queryValue}");

        return new PageContext
        {
            HttpContext = httpContext,
            RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            ActionDescriptor = new CompiledPageActionDescriptor()
        };
    }

    private static PageHandlerExecutingContext MakeExecutingContext(
        MethodInfo? handlerMethod,
        string? queryKey = null,
        string? queryValue = null)
    {
        var pageContext = MakePageContext(queryKey, queryValue);

        HandlerMethodDescriptor? hmd = handlerMethod is not null
            ? new HandlerMethodDescriptor { MethodInfo = handlerMethod }
            : null;

        return new PageHandlerExecutingContext(
            pageContext,
            [],
            hmd,
            new Dictionary<string, object?>(),
            new object());
    }

    private static PageHandlerExecutionDelegate MakeNext(IActionResult? result = null, Exception? ex = null)
    {
        return () =>
        {
            var pageContext = MakePageContext();
            var executed = new PageHandlerExecutedContext(pageContext, [], null, new object())
            {
                Result = result,
                Exception = ex
            };
            return Task.FromResult(executed);
        };
    }

    // Page model methods used to carry [CacheWeave] via reflection
    private class FakePage : PageModel
    {
        [CacheWeave("page-key")]
        public IActionResult OnGet() => new OkResult();

        [CacheWeave("page-key", SlidingExpiry = true, ExpirySeconds = 60)]
        public IActionResult OnGetSliding() => new OkResult();

        [CacheWeave("page-key", IncludeQueryParams = true)]
        public IActionResult OnGetWithQuery() => new OkResult();

        [CacheWeave("page-key", IncludeQueryParams = true, ExcludeParams = ["secret"])]
        public IActionResult OnGetExcludeParam() => new OkResult();

        [CacheWeave("page-key", ExpirySeconds = 0)]
        public IActionResult OnGetNoExpiry() => new OkResult();

        public IActionResult OnGetUndecorated() => new OkResult();
    }

    // -------------------------------------------------------------------------
    // OnPageHandlerSelectionAsync — always completes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnPageHandlerSelectionAsync_CompletesWithoutError()
    {
        var sut = MakeSut();
        await sut.OnPageHandlerSelectionAsync(
            new PageHandlerSelectedContext(MakePageContext(), [], new object()));
    }

    // -------------------------------------------------------------------------
    // No attribute — pass-through
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnPageHandlerExecutionAsync_PassesThrough_WhenNoAttribute()
    {
        var sut = MakeSut();
        var method = typeof(FakePage).GetMethod(nameof(FakePage.OnGetUndecorated))!;
        var ctx = MakeExecutingContext(method);
        var nextCalled = false;
        PageHandlerExecutionDelegate wrappedNext = () => { nextCalled = true; return MakeNext()(); };

        await sut.OnPageHandlerExecutionAsync(ctx, wrappedNext);

        nextCalled.Should().BeTrue();
        _provider.Verify(p => p.GetAsync(It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task OnPageHandlerExecutionAsync_PassesThrough_WhenHandlerMethodIsNull()
    {
        var sut = MakeSut();
        var ctx = MakeExecutingContext(handlerMethod: null);
        var nextCalled = false;
        PageHandlerExecutionDelegate wrappedNext = () => { nextCalled = true; return MakeNext()(); };

        await sut.OnPageHandlerExecutionAsync(ctx, wrappedNext);

        nextCalled.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Cache hit
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnPageHandlerExecutionAsync_ReturnsCachedResult_OnHit()
    {
        var cached = _serializer.Serialize(new { id = 1 });
        _provider.Setup(p => p.GetAsync("page-key", default)).ReturnsAsync(cached);

        var sut = MakeSut();
        var method = typeof(FakePage).GetMethod(nameof(FakePage.OnGet))!;
        var ctx = MakeExecutingContext(method);
        var nextCalled = false;
        PageHandlerExecutionDelegate wrappedNext = () => { nextCalled = true; return MakeNext()(); };

        await sut.OnPageHandlerExecutionAsync(ctx, wrappedNext);

        nextCalled.Should().BeFalse();
        ctx.Result.Should().BeOfType<ContentResult>()
            .Which.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task OnPageHandlerExecutionAsync_CachedResult_HasJsonContentType()
    {
        var cached = _serializer.Serialize(new { id = 1 });
        _provider.Setup(p => p.GetAsync("page-key", default)).ReturnsAsync(cached);

        var sut = MakeSut();
        var method = typeof(FakePage).GetMethod(nameof(FakePage.OnGet))!;
        var ctx = MakeExecutingContext(method);

        await sut.OnPageHandlerExecutionAsync(ctx, MakeNext());

        ctx.Result.Should().BeOfType<ContentResult>()
            .Which.ContentType.Should().Be("application/json");
    }

    // -------------------------------------------------------------------------
    // Sliding expiry on hit
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnPageHandlerExecutionAsync_RefreshesTtl_OnHit_WhenSlidingExpiry()
    {
        var cached = _serializer.Serialize(new { id = 1 });
        _provider.Setup(p => p.GetAsync("page-key", default)).ReturnsAsync(cached);

        var sut = MakeSut();
        var method = typeof(FakePage).GetMethod(nameof(FakePage.OnGetSliding))!;
        var ctx = MakeExecutingContext(method);

        await sut.OnPageHandlerExecutionAsync(ctx, MakeNext());

        _provider.Verify(p => p.SetAsync("page-key", cached, TimeSpan.FromSeconds(60), default), Times.Once);
    }

    // -------------------------------------------------------------------------
    // Cache miss → write
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnPageHandlerExecutionAsync_WritesToCache_OnMiss_WithObjectResult()
    {
        _provider.Setup(p => p.GetAsync("page-key", default)).ReturnsAsync((string?)null);

        var sut = MakeSut();
        var method = typeof(FakePage).GetMethod(nameof(FakePage.OnGet))!;
        var ctx = MakeExecutingContext(method);

        await sut.OnPageHandlerExecutionAsync(ctx, MakeNext(new OkObjectResult(new { id = 1 })));

        _provider.Verify(p => p.SetAsync("page-key", It.IsAny<string>(), It.IsAny<TimeSpan?>(), default), Times.Once);
    }

    [Fact]
    public async Task OnPageHandlerExecutionAsync_WritesToCache_OnMiss_WithJsonResult()
    {
        _provider.Setup(p => p.GetAsync("page-key", default)).ReturnsAsync((string?)null);

        var sut = MakeSut();
        var method = typeof(FakePage).GetMethod(nameof(FakePage.OnGet))!;
        var ctx = MakeExecutingContext(method);

        await sut.OnPageHandlerExecutionAsync(ctx, MakeNext(new JsonResult(new { id = 2 })));

        _provider.Verify(p => p.SetAsync("page-key", It.IsAny<string>(), It.IsAny<TimeSpan?>(), default), Times.Once);
    }

    [Fact]
    public async Task OnPageHandlerExecutionAsync_DoesNotWrite_WhenResultTypeIsUnknown()
    {
        _provider.Setup(p => p.GetAsync("page-key", default)).ReturnsAsync((string?)null);

        var sut = MakeSut();
        var method = typeof(FakePage).GetMethod(nameof(FakePage.OnGet))!;
        var ctx = MakeExecutingContext(method);

        await sut.OnPageHandlerExecutionAsync(ctx, MakeNext(new NoContentResult()));

        _provider.Verify(p => p.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), default), Times.Never);
    }

    // -------------------------------------------------------------------------
    // Exception / null result — not cached
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnPageHandlerExecutionAsync_DoesNotCache_WhenActionThrows()
    {
        _provider.Setup(p => p.GetAsync("page-key", default)).ReturnsAsync((string?)null);

        var sut = MakeSut();
        var method = typeof(FakePage).GetMethod(nameof(FakePage.OnGet))!;
        var ctx = MakeExecutingContext(method);

        await sut.OnPageHandlerExecutionAsync(ctx, MakeNext(ex: new InvalidOperationException("boom")));

        _provider.Verify(p => p.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), default), Times.Never);
    }

    [Fact]
    public async Task OnPageHandlerExecutionAsync_DoesNotCache_WhenResultIsNull()
    {
        _provider.Setup(p => p.GetAsync("page-key", default)).ReturnsAsync((string?)null);

        var sut = MakeSut();
        var method = typeof(FakePage).GetMethod(nameof(FakePage.OnGet))!;
        var ctx = MakeExecutingContext(method);

        await sut.OnPageHandlerExecutionAsync(ctx, MakeNext(result: null));

        _provider.Verify(p => p.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), default), Times.Never);
    }

    // -------------------------------------------------------------------------
    // Query param inclusion
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnPageHandlerExecutionAsync_AppendsQueryParams_ToKey()
    {
        _provider.Setup(p => p.GetAsync(It.IsAny<string>(), default)).ReturnsAsync((string?)null);

        var sut = MakeSut();
        var method = typeof(FakePage).GetMethod(nameof(FakePage.OnGetWithQuery))!;
        var ctx = MakeExecutingContext(method, queryKey: "id", queryValue: "42");

        await sut.OnPageHandlerExecutionAsync(ctx, MakeNext(new OkObjectResult(new { id = 42 })));

        _provider.Verify(p => p.GetAsync("page-key:id=42", default), Times.Once);
    }

    [Fact]
    public async Task OnPageHandlerExecutionAsync_ExcludesSpecifiedQueryParams()
    {
        _provider.Setup(p => p.GetAsync(It.IsAny<string>(), default)).ReturnsAsync((string?)null);

        var sut = MakeSut();
        var method = typeof(FakePage).GetMethod(nameof(FakePage.OnGetExcludeParam))!;

        var pageContext = new PageContext
        {
            HttpContext = new DefaultHttpContext(),
            RouteData = new Microsoft.AspNetCore.Routing.RouteData(),
            ActionDescriptor = new CompiledPageActionDescriptor()
        };
        pageContext.HttpContext.Request.QueryString = new QueryString("?id=1&secret=abc");

        var hmd = new HandlerMethodDescriptor { MethodInfo = method };
        var ctx = new PageHandlerExecutingContext(
            pageContext, [], hmd, new Dictionary<string, object?>(), new object());

        await sut.OnPageHandlerExecutionAsync(ctx, MakeNext(new OkObjectResult(new { id = 1 })));

        // "secret" excluded; only "id=1" in key
        _provider.Verify(p => p.GetAsync("page-key:id=1", default), Times.Once);
    }

    // -------------------------------------------------------------------------
    // KeyVersion appended when set
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnPageHandlerExecutionAsync_AppendsKeyVersion_WhenSet()
    {
        var opts = new CacheWeaveOptions { KeyVersion = "v2" };
        var sut = MakeSut(opts);

        _provider.Setup(p => p.GetAsync(It.IsAny<string>(), default)).ReturnsAsync((string?)null);

        var method = typeof(FakePage).GetMethod(nameof(FakePage.OnGet))!;
        var ctx = MakeExecutingContext(method);

        await sut.OnPageHandlerExecutionAsync(ctx, MakeNext(new OkObjectResult(new { id = 1 })));

        _provider.Verify(p => p.GetAsync("page-key:v2", default), Times.Once);
    }

    // -------------------------------------------------------------------------
    // Expiry resolution
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnPageHandlerExecutionAsync_UsesNullExpiry_WhenExpirySecondsIsZero()
    {
        _provider.Setup(p => p.GetAsync("page-key", default)).ReturnsAsync((string?)null);

        var sut = MakeSut();
        var method = typeof(FakePage).GetMethod(nameof(FakePage.OnGetNoExpiry))!;
        var ctx = MakeExecutingContext(method);

        await sut.OnPageHandlerExecutionAsync(ctx, MakeNext(new OkObjectResult(new { id = 1 })));

        _provider.Verify(p => p.SetAsync("page-key", It.IsAny<string>(), null, default), Times.Once);
    }
}
