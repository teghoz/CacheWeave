namespace CacheWeave.Core;

/// <summary>
/// Controls when a response should be excluded from caching.
/// </summary>
[Flags]
public enum NoCacheCondition
{
    /// <summary>Always cache the response regardless of status or content.</summary>
    Never = 0,

    /// <summary>Do not cache responses with a non-2xx HTTP status code.</summary>
    OnError = 1,

    /// <summary>Do not cache responses where the result value is null or an empty collection.</summary>
    OnEmpty = 2,

    /// <summary>Do not cache on error or empty result. This is the default.</summary>
    OnErrorOrEmpty = OnError | OnEmpty
}
