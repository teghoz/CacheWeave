using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using CacheWeave.Legacy.Abstractions;

namespace CacheWeave.Legacy.Services
{
    public sealed class InProcessStampedeProtector : ICacheStampedeProtector
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
            new ConcurrentDictionary<string, SemaphoreSlim>();

        [return: MaybeNull]
        public async Task<T> ExecuteAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            CancellationToken cancellationToken = default)
        {
            var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await factory(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                semaphore.Release();
                _locks.TryRemove(key, out _);
            }
        }
    }
}
