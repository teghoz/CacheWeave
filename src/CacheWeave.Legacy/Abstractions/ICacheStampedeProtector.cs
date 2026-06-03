using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace CacheWeave.Legacy.Abstractions
{
    public interface ICacheStampedeProtector
    {
        [return: MaybeNull]
        Task<T> ExecuteAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            CancellationToken cancellationToken = default);
    }
}
