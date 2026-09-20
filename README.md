![CacheWeave](https://raw.githubusercontent.com/teghoz/CacheWeave/main/assets/logo-sm.png)

[![CI](https://github.com/teghoz/CacheWeave/actions/workflows/ci.yml/badge.svg)](https://github.com/teghoz/CacheWeave/actions/workflows/ci.yml)

# CacheWeave

Provider-agnostic, declarative response caching for ASP.NET Core.

Decorate any controller action, Razor Page handler, or Minimal API endpoint with `[CacheWeave]` and responses are cached automatically — no boilerplate, no manual cache key management, no stampede risk.

```csharp
[HttpGet]
[CacheWeave("products", ExpirySeconds = 300)]
public async Task<IActionResult> GetProducts([FromQuery] int page = 1)
    => Ok(await _mediator.Send(new GetProductsQuery { Page = page }));
```

---

## Features

| Feature | Detail |
|---|---|
| **Attribute-based caching** | `[CacheWeave]` on any MVC action, Razor Page, or Minimal API endpoint |
| **Automatic key derivation** | Omit the key and CacheWeave derives it from `ControllerName.ActionName` via reflection |
| **Eviction** | `[CacheWeaveEvict]` by exact key or prefix, post-action, with multi-attribute support |
| **Stampede protection** | Per-key `SemaphoreSlim` by default; swap for a distributed lock in multi-instance deployments |
| **Conditional caching** | `NoCacheWhen` flags — skip caching on errors, empty results, or both |
| **Sliding expiry** | Emulated by re-writing the entry on every cache hit |
| **Route parameters** | Route values (e.g. `{id}`) are automatically included in cache keys — framework keys (`controller`, `action`, `page`, `area`) are excluded |
| **Body hashing** | SHA-256 hash of the request body (or selected fields) appended to the key for POST endpoints |
| **Compression** | Optional GZip compression before storage — transparent to callers |
| **Serializer choice** | System.Text.Json (default) or Newtonsoft.Json, configured globally |
| **OpenTelemetry** | Hit/miss/set/eviction counters + duration histogram + Activity spans, all opt-in |
| **Configurable logging** | Diagnostic log level set globally — surface cache activity at `Information` in production |
| **Programmatic API** | `ICacheWeaveService` for `GetOrSetAsync`, `SetAsync`, `InvalidateAsync` |
| **Fault-tolerant** | All cache I/O wrapped in try/catch — Redis outage degrades to cache-miss behaviour, never a 500 |
| **7 providers** | Redis, InMemory, SQLite, NCache, DynamoDB, Memcached, FASTER KV |
| **Multi-target** | `net8.0`, `net9.0`, `net10.0` |
| **.NET Framework 4.8** | `CacheWeave.Legacy` — programmatic caching for net48 without ASP.NET Core |

---

## Quick Start

### 1. Install

```bash
dotnet add package CacheWeave.Core
dotnet add package CacheWeave.Redis   # or CacheWeave.InMemory, etc.
```

### 2. Register

```csharp
// Program.cs
builder.Services.AddCacheWeave(options =>
{
    options.Serializer         = CacheWeaveSerializerType.SystemTextJson; // default
    options.GlobalKeyPrefix    = "my-app";   // e.g. "my-app:products:list"
    options.KeyVersion         = "v1";
    options.DefaultExpiry      = TimeSpan.FromMinutes(5);
    options.EnableMetrics      = true;
    options.DiagnosticLogLevel = LogLevel.Debug;
    options.EnableCompression  = false;
});

builder.Services.AddCacheWeaveRedis("localhost:6379");
// or: builder.Services.AddCacheWeaveInMemory();

// Wire [CacheWeave] and [CacheWeaveEvict] attributes into the MVC pipeline
builder.Services.AddControllers().AddCacheWeaveFilters();
```

### 3. Decorate

```csharp
// Explicit key
[HttpGet]
[CacheWeave("products:list", ExpirySeconds = 300, IncludeQueryParams = true)]
public Task<IActionResult> GetAll([FromQuery] int page = 1) { ... }

// Derived key — resolves to "Products.GetAll" automatically
[HttpGet]
[CacheWeave]
public Task<IActionResult> GetAll() { ... }

// Route parameters — {id} is automatically included in the cache key
// GET /products/42 → key: "products:id=42"
[HttpGet("{id}")]
[CacheWeave("products")]
public Task<IActionResult> GetById(int id) { ... }

// Route params + query params combined
// GET /products/42/reviews?page=2 → key: "products:reviews:id=42:page=2"
[HttpGet("{id}/reviews")]
[CacheWeave("products:reviews")]
public Task<IActionResult> GetReviews(int id, [FromQuery] int page = 1) { ... }

// Exclude specific route params from the key
[HttpGet("{id}/{version}")]
[CacheWeave("products", ExcludeRouteParams = ["version"])]
public Task<IActionResult> GetById(int id, string version) { ... }

// Disable route params entirely
[HttpGet("{id}")]
[CacheWeave("products", IncludeRouteParams = false)]
public Task<IActionResult> GetById(int id) { ... }

// Minimal API with route parameters
app.MapGet("/products/{id}", (int id) => ...)
   .WithCacheWeave("products");

// Minimal API — exclude specific route params
app.MapGet("/products/{id}/{version}", (int id, string version) => ...)
   .WithCacheWeave("products", excludeRouteParams: ["version"]);

// Evict on mutation
[HttpPost]
[CacheWeaveEvict(Prefix = "products:")]
public Task<IActionResult> Create([FromBody] CreateProductCommand cmd) { ... }
```

---

## Configuration Reference

All options are set via `AddCacheWeave(options => ...)`:

| Property | Type | Default | Description |
|---|---|---|---|
| `KeySeparator` | `string` | `":"` | Separator between key segments |
| `GlobalKeyPrefix` | `string?` | `null` | Prepended to every key — use to namespace keys when multiple apps share one Redis |
| `KeyVersion` | `string?` | `null` | Global version injected after the base key |
| `DefaultExpiry` | `TimeSpan?` | `5 min` | TTL when not set on the attribute |
| `DefaultNoCacheCondition` | `NoCacheCondition` | `OnErrorOrEmpty` | Global skip-cache rule |
| `Serializer` | `CacheWeaveSerializerType` | `SystemTextJson` | `SystemTextJson` or `NewtonsoftJson` |
| `EnableCompression` | `bool` | `false` | GZip compress values before storage |
| `EnableMetrics` | `bool` | `true` | Emit OTel metrics and Activity spans |
| `DiagnosticLogLevel` | `LogLevel` | `Debug` | Log level for internal diagnostics |

---

## Attribute Reference

### `[CacheWeave]`

| Property | Type | Default | Description |
|---|---|---|---|
| `Key` (ctor) | `string?` | `null` | Base key. Omit to derive from controller/action name |
| `ExpirySeconds` | `int` | `-1` (global) | TTL. `0` = no expiry |
| `IncludeRouteParams` | `bool` | `true` | Append sorted route params (path segments) to key |
| `ExcludeRouteParams` | `string[]` | `[]` | Route params to strip from key |
| `IncludeQueryParams` | `bool` | `true` | Append sorted query params to key |
| `ExcludeParams` | `string[]` | `[]` | Query params to strip from key |
| `HashBody` | `bool` | `false` | Append SHA-256 body hash to key |
| `HashBodyFields` | `string[]` | `[]` | Hash only these fields (empty = entire body) |
| `SlidingExpiry` | `bool` | `false` | Reset TTL on every cache hit |
| `NoCacheWhen` | `NoCacheCondition` | `OnErrorOrEmpty` | Skip caching condition |

### `[CacheWeaveEvict]`

| Property | Type | Default | Description |
|---|---|---|---|
| `Key` | `string?` | `null` | Exact key to evict |
| `Prefix` | `string?` | `null` | Evict all keys with this prefix |
| `EvictOnFailure` | `bool` | `false` | Evict even when the action fails |

Multiple `[CacheWeaveEvict]` attributes are allowed on a single action.

---

## Providers

| Package | Backing store | `RemoveByPrefix` | Target frameworks |
|---|---|---|---|
| `CacheWeave.Redis` | StackExchange.Redis | Yes (SCAN + DEL) | net8/9/10 |
| `CacheWeave.InMemory` | `IMemoryCache` | Yes (prefix scan) | net8/9/10 |
| `CacheWeave.SQLite` | Microsoft.Data.Sqlite | Yes (SQL LIKE) | net8/9/10 |
| `CacheWeave.NCache` | Alachisoft NCache | No | net8/9/10 |
| `CacheWeave.DynamoDB` | AWS DynamoDB | No | net8/9/10 |
| `CacheWeave.Memcached` | EnyimMemcachedCore | No | net8/9/10 |
| `CacheWeave.Faster` | Microsoft FASTER KV | No | net8/9/10 |
| `CacheWeave.Legacy` | Redis, InMemory, SQLite, DynamoDB, NCache | Depends on provider | **net48** |

### Redis connection behaviour

Redis is treated as optional. `AddCacheWeaveRedis(connectionString)` parses the connection string and then sets:

| Setting | Value | Effect |
|---|---|---|
| `AbortOnConnectFail` | `false` | Startup never fails because Redis is down; the multiplexer keeps reconnecting in the background |
| `BacklogPolicy` | `FailFast` | While disconnected, cache commands fail immediately and the request proceeds as a cache miss, instead of each command queueing for up to `syncTimeout` |

All other settings (`connectTimeout`, `connectRetry`, `syncTimeout`, `asyncTimeout`, `ssl`, `password`, …) come from the connection string. For anything a connection string can't express, pass a `configure` callback — it runs after the defaults above and can override them:

```csharp
builder.Services.AddCacheWeaveRedis("my-redis:6380,ssl=true",
    configureConnection: redis =>
    {
        redis.ConnectTimeout = 1000;
        redis.BacklogPolicy  = BacklogPolicy.Default; // opt back in to queueing during brief disconnects
    },
    configureCache: cache =>
    {
        cache.EvictionRetryWindow = TimeSpan.FromSeconds(3);
    });

// Or bring your own multiplexer (Sentinel, Azure token auth, shared with the rest of your app)
builder.Services.AddCacheWeaveRedis(existingMultiplexer);
builder.Services.AddCacheWeaveRedis(existingMultiplexer, cache => cache.ScanPageSize = 1000);
```

Provider behaviour is configured separately from the connection, via `RedisCacheOptions`:

| Property | Type | Default | Description |
|---|---|---|---|
| `EvictionRetryWindow` | `TimeSpan` | `1.5s` | How long an eviction waits for a reconnect before giving up. `TimeSpan.Zero` disables the retry |
| `ReconnectPollInterval` | `TimeSpan` | `50ms` | How often the connection is polled while inside that window |
| `ScanPageSize` | `int` | `250` | SCAN page size and DEL batch size for prefix eviction |

If an `IConnectionMultiplexer` is already registered when `AddCacheWeaveRedis(connectionString)` is called, CacheWeave uses it rather than creating its own.

#### Evictions during a brief outage

Fail-fast is the right trade for reads and writes — a dropped one is just a cache miss. Evictions are
different: a `[CacheWeaveEvict]` delete that never reaches Redis leaves the old entry being served
until it expires. So evictions, and only evictions, wait up to **1.5s** for the multiplexer to
reconnect and then retry once. A failover or short network blip therefore still invalidates, while a
genuine outage costs a write request 1.5s at most, once per eviction, instead of `syncTimeout` on
every operation. Tune that window with `EvictionRetryWindow`, or set it to `TimeSpan.Zero` to opt out
of the retry entirely.

If the retry doesn't get through, `CacheWeaveEvictFilter` logs a warning (`eviction failed … cache
may be stale`) and the request still succeeds. Watch for that warning if you need to know when the
cache and your data have diverged.

---

## .NET Framework 4.8 Support (`CacheWeave.Legacy`)

`CacheWeave.Legacy` brings provider-agnostic caching to .NET Framework 4.8 applications that cannot migrate to modern .NET. It exposes the same `ICacheWeaveService` programmatic API but **does not include ASP.NET Core filters** — there is no attribute-based caching on net48.

### Install

```bash
dotnet add package CacheWeave.Legacy
```

### Register

```csharp
// Works with any DI container that supports Microsoft.Extensions.DependencyInjection
services
    .AddCacheWeave(options =>
    {
        options.GlobalKeyPrefix = "my-app";
        options.DefaultExpiry   = TimeSpan.FromMinutes(5);
        options.Serializer      = CacheWeaveSerializerType.SystemTextJson;
    })
    .AddCacheWeaveRedis("localhost:6379");
    // or: .AddCacheWeaveInMemory()
    // or: .AddCacheWeaveSQLite(o => o.DatabasePath = "cache.db")
    // or: .AddCacheWeaveDynamoDb(dynamoClient)
    // or: .AddCacheWeaveNCache("my-cache")
```

### Use

```csharp
public class ProductService
{
    private readonly ICacheWeaveService _cache;

    public ProductService(ICacheWeaveService cache) => _cache = cache;

    public Task<Product> GetAsync(int id) =>
        _cache.GetOrSetAsync(
            $"products:{id}",
            ct => _repo.FindAsync(id, ct),
            expiry: TimeSpan.FromMinutes(10));

    public Task InvalidateAsync(int id) =>
        _cache.InvalidateAsync($"products:{id}");
}
```

### Provider capability on net48

| Provider | `RemoveByPrefix` | Notes |
|---|---|---|
| Redis | Yes (SCAN + DEL) | |
| InMemory | No | Uses `System.Runtime.Caching.MemoryCache` (in-box on net48) |
| SQLite | Yes (SQL LIKE) | Uses `System.Data.SQLite.Core` |
| DynamoDB | No | Client-side TTL enforcement included |
| NCache | No | |

---

## OpenTelemetry

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(b => b.AddCacheWeaveMeter())
    .WithTracing(b => b.AddCacheWeaveInstrumentation());
```

Metrics emitted:

| Metric | Type | Description |
|---|---|---|
| `cacheweave.hits` | Counter | Cache hits |
| `cacheweave.misses` | Counter | Cache misses |
| `cacheweave.sets` | Counter | Cache writes |
| `cacheweave.evictions` | Counter | Explicit evictions |
| `cacheweave.duration` | Histogram (ms) | Time to resolve a cache operation |

All instruments carry `cache.key` (or `cache.prefix`) tags.

---

## Programmatic API

```csharp
public class ProductService(ICacheWeaveService cache)
{
    public Task<Product?> GetAsync(Guid id) =>
        cache.GetOrSetAsync(
            $"products:{id}",
            ct => _repo.FindAsync(id, ct),
            expiry: TimeSpan.FromMinutes(10));

    public Task InvalidateAsync(Guid id) =>
        cache.InvalidateAsync($"products:{id}");
}
```

---

## License

MIT — see [LICENSE](LICENSE).
