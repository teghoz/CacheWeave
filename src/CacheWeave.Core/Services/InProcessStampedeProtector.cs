using System.Collections.Concurrent;
using CacheWeave.Core.Abstractions;

namespace CacheWeave.Core.Services;

/// <summary>
/// In-process stampede protector using per-key <see cref="SemaphoreSlim"/> instances.
/// Safe for single-instance deployments. For multi-instance, replace with a distributed
/// lock implementation and register it as <see cref="ICacheStampedeProtector"/>.
/// </summary>
public sealed class InProcessStampedeProtector : ICacheStampedeProtector, IDisposable
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<T?> ExecuteAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken);
        try
        {
            return await factory(cancellationToken);
        }
        finally
        {
            semaphore.Release();
            // Clean up to avoid unbounded growth — safe because the next caller
            // will re-create the semaphore via GetOrAdd
            _locks.TryRemove(key, out _);
        }
    }

    public void Dispose()
    {
        foreach (var semaphore in _locks.Values)
            semaphore.Dispose();

        _locks.Clear();
    }
}
