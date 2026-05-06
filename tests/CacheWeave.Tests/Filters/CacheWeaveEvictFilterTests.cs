using CacheWeave.Core;
using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Filters;
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

public class CacheWeaveEvictFilterTests
{
    private readonly Mock<ICacheProvider> _provider = new();
    private readonly CacheWeaveOptions _opts = new() { EnableMetrics = false };

    private CacheWeaveEvictFilter MakeSut() => new(
        _provider.Object,
        Options.Create(_opts),
        NullLogger<CacheWeaveEvictFilter>.Instance);

    // Controllers used to carry [CacheWeaveEvict] via reflection
    private class EvictByKeyController : ControllerBase
    {
        [CacheWeaveEvict(Key = "products:1")]
        public IActionResult Delete() => Ok();
    }

    private class EvictByPrefixController : ControllerBase
    {
        [CacheWeaveEvict(Prefix = "products:")]
        public IActionResult Delete() => Ok();
    }

    private class NoEvictController : ControllerBase
    {
        public IActionResult Delete() => Ok();
    }

    private class EvictOnFailureController : ControllerBase
    {
        [CacheWeaveEvict(Key = "k", EvictOnFailure = true)]
        public IActionResult Delete() => Ok();
    }

    private class MultiEvictController : ControllerBase
    {
        [CacheWeaveEvict(Key = "products:list")]
        [CacheWeaveEvict(Key = "dashboard:stats")]
        public IActionResult Update() => Ok();
    }

    private class EvictByPrefixAndKeyController : ControllerBase
    {
        [CacheWeaveEvict(Prefix = "products:")]
        [CacheWeaveEvict(Key = "dashboard:stats")]
        public IActionResult Update() => Ok();
    }

    private static (ActionExecutingContext executing, ActionExecutionDelegate next)
        MakeContext<TController>(bool actionSucceeds = true, string actionName = "Delete")
        where TController : ControllerBase
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
        var executing = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), new object());

        ActionExecutionDelegate next = () =>
        {
            var executed = new ActionExecutedContext(actionContext, [], new object());
            if (!actionSucceeds)
                executed.Exception = new InvalidOperationException("fail");
            else
                executed.Result = new OkResult();
            return Task.FromResult(executed);
        };

        return (executing, next);
    }

    // -------------------------------------------------------------------------
    // Pass-through
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnActionExecutionAsync_PassesThrough_WhenNoEvictAttribute()
    {
        var sut = MakeSut();
        var (ctx, next) = MakeContext<NoEvictController>();
        var nextCalled = false;
        ActionExecutionDelegate wrappedNext = () => { nextCalled = true; return next(); };

        await sut.OnActionExecutionAsync(ctx, wrappedNext);

        nextCalled.Should().BeTrue();
        _provider.Verify(p => p.RemoveAsync(It.IsAny<string>(), default), Times.Never);
    }

    // -------------------------------------------------------------------------
    // Evict by key
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnActionExecutionAsync_EvictsKey_OnSuccess()
    {
        var sut = MakeSut();
        var (ctx, next) = MakeContext<EvictByKeyController>();

        await sut.OnActionExecutionAsync(ctx, next);

        _provider.Verify(p => p.RemoveAsync("products:1", default), Times.Once);
    }

    // -------------------------------------------------------------------------
    // Evict by prefix
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnActionExecutionAsync_EvictsByPrefix_OnSuccess()
    {
        var sut = MakeSut();
        var (ctx, next) = MakeContext<EvictByPrefixController>();

        await sut.OnActionExecutionAsync(ctx, next);

        _provider.Verify(p => p.RemoveByPrefixAsync("products:", default), Times.Once);
    }

    // -------------------------------------------------------------------------
    // Multiple evict attributes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnActionExecutionAsync_EvictsAllKeys_WhenMultipleAttributes()
    {
        var sut = MakeSut();
        var (ctx, next) = MakeContext<MultiEvictController>(actionName: "Update");

        await sut.OnActionExecutionAsync(ctx, next);

        _provider.Verify(p => p.RemoveAsync("products:list", default), Times.Once);
        _provider.Verify(p => p.RemoveAsync("dashboard:stats", default), Times.Once);
    }

    [Fact]
    public async Task OnActionExecutionAsync_EvictsPrefixAndKey_WhenMixed()
    {
        var sut = MakeSut();
        var (ctx, next) = MakeContext<EvictByPrefixAndKeyController>(actionName: "Update");

        await sut.OnActionExecutionAsync(ctx, next);

        _provider.Verify(p => p.RemoveByPrefixAsync("products:", default), Times.Once);
        _provider.Verify(p => p.RemoveAsync("dashboard:stats", default), Times.Once);
    }

    // -------------------------------------------------------------------------
    // EvictOnFailure
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnActionExecutionAsync_SkipsEviction_WhenActionFails_AndEvictOnFailureFalse()
    {
        var sut = MakeSut();
        var (ctx, next) = MakeContext<EvictByKeyController>(actionSucceeds: false);

        await sut.OnActionExecutionAsync(ctx, next);

        _provider.Verify(p => p.RemoveAsync(It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task OnActionExecutionAsync_EvictsEvenOnFailure_WhenEvictOnFailureTrue()
    {
        var sut = MakeSut();
        var (ctx, next) = MakeContext<EvictOnFailureController>(actionSucceeds: false);

        await sut.OnActionExecutionAsync(ctx, next);

        _provider.Verify(p => p.RemoveAsync("k", default), Times.Once);
    }

    // -------------------------------------------------------------------------
    // Neither Key nor Prefix — logs warning, no eviction
    // -------------------------------------------------------------------------

    private class EmptyEvictController : ControllerBase
    {
        [CacheWeaveEvict]
        public IActionResult Delete() => Ok();
    }

    [Fact]
    public async Task OnActionExecutionAsync_LogsWarning_WhenNeitherKeyNorPrefix()
    {
        var sut = MakeSut();
        var (ctx, next) = MakeContext<EmptyEvictController>();

        await sut.OnActionExecutionAsync(ctx, next);

        _provider.Verify(p => p.RemoveAsync(It.IsAny<string>(), default), Times.Never);
        _provider.Verify(p => p.RemoveByPrefixAsync(It.IsAny<string>(), default), Times.Never);
    }

    // -------------------------------------------------------------------------
    // Fault tolerance — RemoveAsync throws, should not propagate
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnActionExecutionAsync_DoesNotThrow_WhenRemoveAsyncThrows()
    {
        _provider.Setup(p => p.RemoveAsync("products:1", default))
            .ThrowsAsync(new InvalidOperationException("Redis down"));

        var sut = MakeSut();
        var (ctx, next) = MakeContext<EvictByKeyController>();

        // Should not throw
        var act = async () => await sut.OnActionExecutionAsync(ctx, next);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OnActionExecutionAsync_DoesNotThrow_WhenRemoveByPrefixAsyncThrows()
    {
        _provider.Setup(p => p.RemoveByPrefixAsync("products:", default))
            .ThrowsAsync(new InvalidOperationException("Redis down"));

        var sut = MakeSut();
        var (ctx, next) = MakeContext<EvictByPrefixController>();

        var act = async () => await sut.OnActionExecutionAsync(ctx, next);
        await act.Should().NotThrowAsync();
    }

    // -------------------------------------------------------------------------
    // Metrics path — EnableMetrics = true records eviction counters
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OnActionExecutionAsync_RecordsMetrics_WhenEnabled()
    {
        var opts = new CacheWeaveOptions { EnableMetrics = true };
        var sut = new CacheWeaveEvictFilter(
            _provider.Object,
            Options.Create(opts),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CacheWeaveEvictFilter>.Instance);

        var (ctx, next) = MakeContext<EvictByKeyController>();

        // Should not throw even when metrics are enabled
        var act = async () => await sut.OnActionExecutionAsync(ctx, next);
        await act.Should().NotThrowAsync();

        _provider.Verify(p => p.RemoveAsync("products:1", default), Times.Once);
    }

    [Fact]
    public async Task OnActionExecutionAsync_RecordsPrefixMetrics_WhenEnabled()
    {
        var opts = new CacheWeaveOptions { EnableMetrics = true };
        var sut = new CacheWeaveEvictFilter(
            _provider.Object,
            Options.Create(opts),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CacheWeaveEvictFilter>.Instance);

        var (ctx, next) = MakeContext<EvictByPrefixController>();

        var act = async () => await sut.OnActionExecutionAsync(ctx, next);
        await act.Should().NotThrowAsync();

        _provider.Verify(p => p.RemoveByPrefixAsync("products:", default), Times.Once);
    }
}
