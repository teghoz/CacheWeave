using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace CacheWeave.Legacy.Abstractions
{
    public interface ICacheWeaveService
    {
        [return: MaybeNull]
        Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

        [return: MaybeNull]
        Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default);

        Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
        Task InvalidateAsync(string key, CancellationToken cancellationToken = default);
        Task InvalidateByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
        Task InvalidateByPrefixesAsync(IEnumerable<string> prefixes, CancellationToken cancellationToken = default);
    }
}
