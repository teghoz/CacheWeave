using CacheWeave.Core;
using CacheWeave.Core.Abstractions;
using CacheWeave.Core.KeyBuilders;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;


namespace CacheWeave.Tests.KeyBuilders;

public class DefaultCacheKeyBuilderTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ActionExecutingContext MakeContext(
        string controllerName = "Products",
        string actionName = "GetAll",
        string? query = null)
    {
        var httpContext = new DefaultHttpContext();
        if (query is not null)
            httpContext.Request.QueryString = new QueryString(query);

        var descriptor = new ControllerActionDescriptor
        {
            ControllerName = controllerName,
            ActionName = actionName
        };

        var actionContext = new ActionContext(httpContext, new RouteData(), descriptor);
        return new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), new object());
    }

    private static DefaultCacheKeyBuilder MakeBuilder(
        string separator = ":",
        string? version = null,
        IKeyContextProvider? contextProvider = null)
    {
        var opts = Options.Create(new CacheWeaveOptions
        {
            KeySeparator = separator,
            KeyVersion = version
        });
        var serializer = new CacheWeave.Core.Serialization.SystemTextJsonCacheSerializer();
        return new DefaultCacheKeyBuilder(opts, serializer, contextProvider);
    }

    // -------------------------------------------------------------------------
    // Explicit key
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_ReturnsExplicitKey_WhenKeyProvided()
    {
        var builder = MakeBuilder();
        var attr = new CacheWeaveAttribute("products") { IncludeQueryParams = false };

        var key = await builder.BuildAsync(attr, MakeContext());

        key.Should().Be("products");
    }

    [Fact]
    public async Task BuildAsync_AppendsVersion_WhenConfigured()
    {
        var builder = MakeBuilder(version: "v2");
        var attr = new CacheWeaveAttribute("products") { IncludeQueryParams = false };

        var key = await builder.BuildAsync(attr, MakeContext());

        key.Should().Be("products:v2");
    }

    [Fact]
    public async Task BuildAsync_UsesCustomSeparator()
    {
        var builder = MakeBuilder(separator: ".", version: "v1");
        var attr = new CacheWeaveAttribute("products") { IncludeQueryParams = false };

        var key = await builder.BuildAsync(attr, MakeContext());

        key.Should().Be("products.v1");
    }

    // -------------------------------------------------------------------------
    // Derived key (no explicit key)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_DerivesKeyFromControllerAndAction_WhenKeyIsNull()
    {
        var builder = MakeBuilder();
        var attr = new CacheWeaveAttribute { IncludeQueryParams = false };

        var key = await builder.BuildAsync(attr, MakeContext("Materials", "GetAll"));

        key.Should().Be("Materials.GetAll");
    }

    [Fact]
    public async Task BuildAsync_DerivedKey_IncludesVersion()
    {
        var builder = MakeBuilder(version: "v3");
        var attr = new CacheWeaveAttribute { IncludeQueryParams = false };

        var key = await builder.BuildAsync(attr, MakeContext("Orders", "GetById"));

        key.Should().Be("Orders.GetById:v3");
    }

    // -------------------------------------------------------------------------
    // Query params
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_AppendsQueryParams_InSortedOrder()
    {
        var builder = MakeBuilder();
        var attr = new CacheWeaveAttribute("products") { IncludeQueryParams = true };

        var key = await builder.BuildAsync(attr, MakeContext(query: "?pageSize=20&pageNumber=1"));

        key.Should().Be("products:pageNumber=1:pageSize=20");
    }

    [Fact]
    public async Task BuildAsync_ExcludesListedQueryParams()
    {
        var builder = MakeBuilder();
        var attr = new CacheWeaveAttribute("products")
        {
            IncludeQueryParams = true,
            ExcludeParams = ["trackingId"]
        };

        var key = await builder.BuildAsync(attr, MakeContext(query: "?pageNumber=1&trackingId=abc"));

        key.Should().Contain("pageNumber=1");
        key.Should().NotContain("trackingId");
    }

    [Fact]
    public async Task BuildAsync_OmitsQueryParams_WhenIncludeQueryParamsFalse()
    {
        var builder = MakeBuilder();
        var attr = new CacheWeaveAttribute("products") { IncludeQueryParams = false };

        var key = await builder.BuildAsync(attr, MakeContext(query: "?pageNumber=1&pageSize=20"));

        key.Should().Be("products");
    }

    [Fact]
    public async Task BuildAsync_OmitsQuerySegment_WhenQueryIsEmpty()
    {
        var builder = MakeBuilder();
        var attr = new CacheWeaveAttribute("products") { IncludeQueryParams = true };

        var key = await builder.BuildAsync(attr, MakeContext());

        key.Should().Be("products");
    }

    // -------------------------------------------------------------------------
    // Context provider
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_AppendsContextSegment_WhenProviderReturnsValue()
    {
        var mockProvider = new Mock<IKeyContextProvider>();
        mockProvider
            .Setup(p => p.GetContextSegmentAsync(It.IsAny<HttpContext>()))
            .ReturnsAsync("tenant=42");

        var builder = MakeBuilder(contextProvider: mockProvider.Object);
        var attr = new CacheWeaveAttribute("products") { IncludeQueryParams = false };

        var key = await builder.BuildAsync(attr, MakeContext());

        key.Should().Be("products:tenant=42");
    }

    [Fact]
    public async Task BuildAsync_OmitsContextSegment_WhenProviderReturnsNull()
    {
        var mockProvider = new Mock<IKeyContextProvider>();
        mockProvider
            .Setup(p => p.GetContextSegmentAsync(It.IsAny<HttpContext>()))
            .ReturnsAsync((string?)null);

        var builder = MakeBuilder(contextProvider: mockProvider.Object);
        var attr = new CacheWeaveAttribute("products") { IncludeQueryParams = false };

        var key = await builder.BuildAsync(attr, MakeContext());

        key.Should().Be("products");
    }

    // -------------------------------------------------------------------------
    // Body hash
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_AppendsBodyHash_WhenHashBodyTrue()
    {
        var builder = MakeBuilder();
        var attr = new CacheWeaveAttribute("search") { IncludeQueryParams = false, HashBody = true };

        var httpContext = new DefaultHttpContext();
        var descriptor = new ControllerActionDescriptor { ControllerName = "X", ActionName = "Y" };
        var actionContext = new ActionContext(httpContext, new RouteData(), descriptor);
        var ctx = new ActionExecutingContext(
            actionContext, [],
            new Dictionary<string, object?> { ["body"] = new { term = "steel" } },
            new object());

        var key = await builder.BuildAsync(attr, ctx);

        key.Should().StartWith("search:");
        key.Length.Should().BeGreaterThan("search:".Length); // hash appended
    }

    [Fact]
    public async Task BuildAsync_NoBodyHash_WhenHashBodyFalseEvenWithArguments()
    {
        var builder = MakeBuilder();
        var attr = new CacheWeaveAttribute("search") { IncludeQueryParams = false, HashBody = false };

        var httpContext = new DefaultHttpContext();
        var descriptor = new ControllerActionDescriptor { ControllerName = "X", ActionName = "Y" };
        var actionContext = new ActionContext(httpContext, new RouteData(), descriptor);
        var ctx = new ActionExecutingContext(
            actionContext, [],
            new Dictionary<string, object?> { ["body"] = new { term = "steel" } },
            new object());

        var key = await builder.BuildAsync(attr, ctx);

        key.Should().Be("search");
    }
}
