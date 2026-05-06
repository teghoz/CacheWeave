using System.Reflection;
using CacheWeave.Core;
using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Filters;
using CacheWeave.Core.KeyBuilders;
using CacheWeave.Core.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CacheWeave.Tests.Configuration;

/// <summary>
/// Verifies that <see cref="CacheWeaveOptions.GlobalKeyPrefix"/> is prepended to every
/// cache key produced by the key builder, page filter, endpoint filter, and evict filter.
/// </summary>
public class GlobalKeyPrefixTests
{
    // =========================================================================
    // DefaultCacheKeyBuilder
    // =========================================================================

    private static DefaultCacheKeyBuilder MakeKeyBuilder(CacheWeaveOptions opts)
        => new(Options.Create(opts), new SystemTextJsonCacheSerializer());

    private static ActionExecutingContext MakeActionContext(string? query = null)
    {
        var httpContext = new DefaultHttpContext();
        if (query is not null)
            httpContext.Request.QueryString = new QueryString(query);

        var descriptor = new ControllerActionDescriptor
        {
            ControllerName = "Products",
            ActionName = "GetAll",
            MethodInfo = typeof(FakeController).GetMethod(nameof(FakeController.GetAll))!
        };
        var actionContext = new ActionContext(httpContext, new RouteData(), descriptor);
        return new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), new object());
    }

    private class FakeController : ControllerBase
    {
        [CacheWeave("products:list")]
        public IActionResult GetAll() => Ok();
    }

    [Fact]
    public async Task KeyBuilder_PrependsGlobalPrefix_ToExplicitKey()
    {
        var opts = new CacheWeaveOptions { GlobalKeyPrefix = "my-app" };
        var builder = MakeKeyBuilder(opts);
        var ctx = MakeActionContext();
        var attr = new CacheWeaveAttribute("products:list");

        var key = await builder.BuildAsync(attr, ctx);

        key.Should().Be("my-app:products:list");
    }

    [Fact]
    public async Task KeyBuilder_PrependsGlobalPrefix_ToDerivedKey()
    {
        var opts = new CacheWeaveOptions { GlobalKeyPrefix = "my-app" };
        var builder = MakeKeyBuilder(opts);
        var ctx = MakeActionContext();
        var attr = new CacheWeaveAttribute(); // derived from controller/action

        var key = await builder.BuildAsync(attr, ctx);

        key.Should().StartWith("my-app:");
    }

    [Fact]
    public async Task KeyBuilder_PrependsGlobalPrefix_BeforeKeyVersion()
    {
        var opts = new CacheWeaveOptions { GlobalKeyPrefix = "my-app", KeyVersion = "v2" };
        var builder = MakeKeyBuilder(opts);
        var ctx = MakeActionContext();
        var attr = new CacheWeaveAttribute("products:list");

        var key = await builder.BuildAsync(attr, ctx);

        key.Should().Be("my-app:products:list:v2");
    }

    [Fact]
    public async Task KeyBuilder_PrependsGlobalPrefix_WithQueryParams()
    {
        var opts = new CacheWeaveOptions { GlobalKeyPrefix = "my-app" };
        var builder = MakeKeyBuilder(opts);
        var ctx = MakeActionContext("?page=2");
        var attr = new CacheWeaveAttribute("products:list") { IncludeQueryParams = true };

        var key = await builder.BuildAsync(attr, ctx);

        key.Should().Be("my-app:products:list:page=2");
    }

    [Fact]
    public async Task KeyBuilder_NoPrefix_WhenGlobalKeyPrefixIsNull()
    {
        var opts = new CacheWeaveOptions { GlobalKeyPrefix = null };
        var builder = MakeKeyBuilder(opts);
        var ctx = MakeActionContext();
        var attr = new CacheWeaveAttribute("products:list");

        var key = await builder.BuildAsync(attr, ctx);

        key.Should().Be("products:list");
    }

    [Fact]
    public async Task KeyBuilder_NoPrefix_WhenGlobalKeyPrefixIsWhitespace()
    {
        var opts = new CacheWeaveOptions { GlobalKeyPrefix = "   " };
        var builder = MakeKeyBuilder(opts);
        var ctx = MakeActionContext();
        var attr = new CacheWeaveAttribute("products:list");

        var key = await builder.BuildAsync(attr, ctx);

        key.Should().Be("products:list");
    }

    [Fact]
    public async Task KeyBuilder_UsesCustomSeparator_WithGlobalPrefix()
    {
        var opts = new CacheWeaveOptions { GlobalKeyPrefix = "my-app", KeySeparator = "|" };
        var builder = MakeKeyBuilder(opts);
        var ctx = MakeActionContext();
        var attr = new CacheWeaveAttribute("products:list");

        var key = await builder.BuildAsync(attr, ctx);

        key.Should().Be("my-app|products:list");
    }

    // =========================================================================
    // CacheWeaveEvictFilter
    // =========================================================================

    private static CacheWeaveEvictFilter MakeEvictFilter(Mock<ICacheProvider> provider, CacheWeaveOptions opts)
        => new(provider.Object, Options.Create(opts), NullLogger<CacheWeaveEvictFilter>.Instance);

    private class EvictKeyController : ControllerBase
    {
        [CacheWeaveEvict(Key = "products:1")]
        public IActionResult Delete() => Ok();
    }

    private class EvictPrefixController : ControllerBase
    {
        [CacheWeaveEvict(Prefix = "products:")]
        public IActionResult Delete() => Ok();
    }

    private static (ActionExecutingContext ctx, ActionExecutionDelegate next)
        MakeEvictContext<TController>(string actionName = "Delete") where TController : ControllerBase
    {
        var httpContext = new DefaultHttpContext();
        var method = typeof(TController).GetMethod(actionName)!;
        var descriptor = new ControllerActionDescriptor
        {
            ControllerName = typeof(TController).Name,
            ActionName = actionName,
            MethodInfo = method
        };
        var actionContext = new ActionContext(httpContext, new RouteData(), descriptor);
        var ctx = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), new object());
        ActionExecutionDelegate next = () =>
        {
            var executed = new ActionExecutedContext(actionContext, [], new object()) { Result = new OkResult() };
            return Task.FromResult(executed);
        };
        return (ctx, next);
    }

    [Fact]
    public async Task EvictFilter_PrependsGlobalPrefix_ToEvictKey()
    {
        var provider = new Mock<ICacheProvider>();
        var opts = new CacheWeaveOptions { GlobalKeyPrefix = "my-app", EnableMetrics = false };
        var sut = MakeEvictFilter(provider, opts);
        var (ctx, next) = MakeEvictContext<EvictKeyController>();

        await sut.OnActionExecutionAsync(ctx, next);

        provider.Verify(p => p.RemoveAsync("my-app:products:1", default), Times.Once);
        provider.Verify(p => p.RemoveAsync("products:1", default), Times.Never);
    }

    [Fact]
    public async Task EvictFilter_PrependsGlobalPrefix_ToEvictPrefix()
    {
        var provider = new Mock<ICacheProvider>();
        var opts = new CacheWeaveOptions { GlobalKeyPrefix = "my-app", EnableMetrics = false };
        var sut = MakeEvictFilter(provider, opts);
        var (ctx, next) = MakeEvictContext<EvictPrefixController>();

        await sut.OnActionExecutionAsync(ctx, next);

        provider.Verify(p => p.RemoveByPrefixAsync("my-app:products:", default), Times.Once);
        provider.Verify(p => p.RemoveByPrefixAsync("products:", default), Times.Never);
    }

    [Fact]
    public async Task EvictFilter_NoPrefix_WhenGlobalKeyPrefixIsNull()
    {
        var provider = new Mock<ICacheProvider>();
        var opts = new CacheWeaveOptions { GlobalKeyPrefix = null, EnableMetrics = false };
        var sut = MakeEvictFilter(provider, opts);
        var (ctx, next) = MakeEvictContext<EvictKeyController>();

        await sut.OnActionExecutionAsync(ctx, next);

        provider.Verify(p => p.RemoveAsync("products:1", default), Times.Once);
    }

    // =========================================================================
    // CacheWeavePageFilter
    // =========================================================================

    private static PageContext MakePageContext()
        => new()
        {
            HttpContext = new DefaultHttpContext(),
            RouteData = new RouteData(),
            ActionDescriptor = new CompiledPageActionDescriptor()
        };

    private class FakePage : PageModel
    {
        [CacheWeave("page-key")]
        public IActionResult OnGet() => new OkResult();
    }

    [Fact]
    public async Task PageFilter_PrependsGlobalPrefix_ToCacheKey()
    {
        var provider = new Mock<ICacheProvider>();
        provider.Setup(p => p.GetAsync(It.IsAny<string>(), default)).ReturnsAsync((string?)null);

        var opts = new CacheWeaveOptions { GlobalKeyPrefix = "my-app" };
        var sut = new CacheWeavePageFilter(
            provider.Object,
            new SystemTextJsonCacheSerializer(),
            Options.Create(opts),
            NullLogger<CacheWeavePageFilter>.Instance);

        var method = typeof(FakePage).GetMethod(nameof(FakePage.OnGet))!;
        var hmd = new HandlerMethodDescriptor { MethodInfo = method };
        var ctx = new PageHandlerExecutingContext(
            MakePageContext(), [], hmd, new Dictionary<string, object?>(), new object());

        PageHandlerExecutionDelegate next = () =>
        {
            var executed = new PageHandlerExecutedContext(MakePageContext(), [], null, new object())
            {
                Result = new OkObjectResult(new { id = 1 })
            };
            return Task.FromResult(executed);
        };

        await sut.OnPageHandlerExecutionAsync(ctx, next);

        provider.Verify(p => p.GetAsync("my-app:page-key", default), Times.Once);
    }

    [Fact]
    public async Task PageFilter_NoPrefix_WhenGlobalKeyPrefixIsNull()
    {
        var provider = new Mock<ICacheProvider>();
        provider.Setup(p => p.GetAsync(It.IsAny<string>(), default)).ReturnsAsync((string?)null);

        var opts = new CacheWeaveOptions { GlobalKeyPrefix = null };
        var sut = new CacheWeavePageFilter(
            provider.Object,
            new SystemTextJsonCacheSerializer(),
            Options.Create(opts),
            NullLogger<CacheWeavePageFilter>.Instance);

        var method = typeof(FakePage).GetMethod(nameof(FakePage.OnGet))!;
        var hmd = new HandlerMethodDescriptor { MethodInfo = method };
        var ctx = new PageHandlerExecutingContext(
            MakePageContext(), [], hmd, new Dictionary<string, object?>(), new object());

        PageHandlerExecutionDelegate next = () =>
        {
            var executed = new PageHandlerExecutedContext(MakePageContext(), [], null, new object())
            {
                Result = new OkObjectResult(new { id = 1 })
            };
            return Task.FromResult(executed);
        };

        await sut.OnPageHandlerExecutionAsync(ctx, next);

        provider.Verify(p => p.GetAsync("page-key", default), Times.Once);
    }

    // =========================================================================
    // CacheWeaveEndpointFilter
    // =========================================================================

    private sealed class FakeEndpointFilterInvocationContext : EndpointFilterInvocationContext
    {
        public FakeEndpointFilterInvocationContext(HttpContext httpContext) { HttpContext = httpContext; }
        public override HttpContext HttpContext { get; }
        public override IList<object?> Arguments => [];
        public override T GetArgument<T>(int index) => throw new NotImplementedException();
    }

    [Fact]
    public async Task EndpointFilter_PrependsGlobalPrefix_ToCacheKey()
    {
        var provider = new Mock<ICacheProvider>();
        provider.Setup(p => p.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var opts = new CacheWeaveOptions { GlobalKeyPrefix = "my-app" };
        var sut = new CacheWeaveEndpointFilter(
            new CacheWeaveAttribute("ep-key"),
            provider.Object,
            new SystemTextJsonCacheSerializer(),
            Options.Create(opts),
            NullLogger<CacheWeaveEndpointFilter>.Instance);

        var ctx = new FakeEndpointFilterInvocationContext(new DefaultHttpContext());

        await sut.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(new { id = 1 }));

        provider.Verify(p => p.GetAsync("my-app:ep-key", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EndpointFilter_NoPrefix_WhenGlobalKeyPrefixIsNull()
    {
        var provider = new Mock<ICacheProvider>();
        provider.Setup(p => p.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var opts = new CacheWeaveOptions { GlobalKeyPrefix = null };
        var sut = new CacheWeaveEndpointFilter(
            new CacheWeaveAttribute("ep-key"),
            provider.Object,
            new SystemTextJsonCacheSerializer(),
            Options.Create(opts),
            NullLogger<CacheWeaveEndpointFilter>.Instance);

        var ctx = new FakeEndpointFilterInvocationContext(new DefaultHttpContext());

        await sut.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>(new { id = 1 }));

        provider.Verify(p => p.GetAsync("ep-key", It.IsAny<CancellationToken>()), Times.Once);
    }
}
