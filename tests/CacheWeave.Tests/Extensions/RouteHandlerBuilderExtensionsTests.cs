using CacheWeave.Core;
using CacheWeave.Core.Abstractions;
using CacheWeave.Core.Extensions;
using CacheWeave.Core.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CacheWeave.Tests.Extensions;

public class RouteHandlerBuilderExtensionsTests
{
    // Build a minimal DI container with all services CacheWeaveEndpointFilter needs
    private static IServiceProvider BuildServiceProvider(Mock<ICacheProvider>? providerMock = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();

        var provider = providerMock ?? new Mock<ICacheProvider>();
        services.AddSingleton(provider.Object);
        services.AddSingleton<ICacheSerializer, SystemTextJsonCacheSerializer>();
        services.Configure<CacheWeaveOptions>(_ => { });

        return services.BuildServiceProvider();
    }

    // Build a RouteHandlerBuilder backed by a real WebApplication so AddEndpointFilter works
    private static (RouteHandlerBuilder builder, IServiceProvider sp) MakeBuilder(
        Mock<ICacheProvider>? providerMock = null)
    {
        var sp = BuildServiceProvider(providerMock);

        var app = WebApplication.CreateBuilder().Build();
        // Register the services into the app's DI
        var appSp = BuildServiceProvider(providerMock);

        // We need a RouteHandlerBuilder — create one via MapGet on a minimal WebApplication
        var webApp = WebApplication.Create();
        var rhb = webApp.MapGet("/test", () => Results.Ok(new { id = 1 }));
        return (rhb, appSp);
    }

    // -------------------------------------------------------------------------
    // WithCacheWeave — returns the same builder (fluent)
    // -------------------------------------------------------------------------

    [Fact]
    public void WithCacheWeave_ReturnsSameBuilder()
    {
        var (builder, _) = MakeBuilder();

        var returned = builder.WithCacheWeave("test-key");

        returned.Should().BeSameAs(builder);
    }

    [Fact]
    public void WithCacheWeave_WithAllOptions_ReturnsSameBuilder()
    {
        var (builder, _) = MakeBuilder();

        var returned = builder.WithCacheWeave(
            key: "test-key",
            expirySeconds: 300,
            includeQueryParams: false,
            excludeParams: ["token"],
            hashBody: true,
            hashBodyFields: ["id"],
            slidingExpiry: true,
            noCacheWhen: NoCacheCondition.Never);

        returned.Should().BeSameAs(builder);
    }

    // -------------------------------------------------------------------------
    // WithCacheWeave — filter is invoked on request (integration-style)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WithCacheWeave_FilterServesCache_OnHit()
    {
        var providerMock = new Mock<ICacheProvider>();
        var serializer = new SystemTextJsonCacheSerializer();
        var cached = serializer.Serialize(new { id = 99 });
        providerMock.Setup(p => p.GetAsync("test-key", It.IsAny<CancellationToken>())).ReturnsAsync(cached);

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(providerMock.Object);
        builder.Services.AddSingleton<ICacheSerializer>(serializer);
        builder.Services.Configure<CacheWeaveOptions>(_ => { });
        builder.Services.AddLogging();

        var app = builder.Build();
        app.MapGet("/cached", () => Results.Ok(new { id = 1 }))
           .WithCacheWeave("test-key");

        // Invoke the filter pipeline directly via the endpoint filter delegate
        // by resolving the filter from DI and calling InvokeAsync
        var sp = app.Services;
        var filter = new CacheWeave.Core.Filters.CacheWeaveEndpointFilter(
            new CacheWeaveAttribute("test-key"),
            providerMock.Object,
            serializer,
            sp.GetRequiredService<IOptions<CacheWeaveOptions>>(),
            sp.GetRequiredService<ILogger<CacheWeave.Core.Filters.CacheWeaveEndpointFilter>>());

        var httpContext = new DefaultHttpContext();
        var ctx = new FakeEndpointFilterInvocationContext(httpContext);
        var nextCalled = false;
        EndpointFilterDelegate next = _ => { nextCalled = true; return ValueTask.FromResult<object?>(null); };

        var result = await filter.InvokeAsync(ctx, next);

        nextCalled.Should().BeFalse();
        result.Should().NotBeNull();
    }

    private sealed class FakeEndpointFilterInvocationContext : EndpointFilterInvocationContext
    {
        public FakeEndpointFilterInvocationContext(HttpContext httpContext) { HttpContext = httpContext; }
        public override HttpContext HttpContext { get; }
        public override IList<object?> Arguments => [];
        public override T GetArgument<T>(int index) => throw new NotImplementedException();
    }
}
