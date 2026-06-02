using System;
using System.Threading;
using System.Threading.Tasks;

namespace CacheWeave.Legacy.Abstractions
{
    public interface ICacheProvider
    {
        Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
        Task SetAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
        Task RemoveAsync(string key, CancellationToken cancellationToken = default);
        Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
    }
}
