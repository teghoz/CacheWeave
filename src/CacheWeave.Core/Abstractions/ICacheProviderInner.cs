namespace CacheWeave.Core.Abstractions;

/// <summary>
/// Marker interface used internally by CacheWeave to resolve the concrete provider
/// before the optional compression decorator is applied.
/// Provider packages (Redis, InMemory, etc.) register their implementation against
/// both <see cref="ICacheProviderInner"/> and (via the decorator) <see cref="ICacheProvider"/>.
/// Consumer code should always depend on <see cref="ICacheProvider"/>.
/// </summary>
public interface ICacheProviderInner : ICacheProvider
{
}
