using CacheWeave.Core.Abstractions;

namespace CacheWeave.Core.Providers;

/// <summary>
/// A no-op <see cref="ICacheProvider"/> used when <see cref="CacheWeaveOptions.Enabled"/> is <c>false</c>.
/// All reads return <c>null</c> (cache miss). All writes and evictions are silently discarded.
/// </summary>
internal sealed class DisabledCacheProvider : ICacheProvider
{
    public static readonly DisabledCacheProvider Instance = new();

    private DisabledCacheProvider() { }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task SetAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
