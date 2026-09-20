using System;

namespace CacheWeave.Legacy.Providers
{
    /// <summary>
    /// Tuning knobs for <see cref="RedisCacheProvider"/>. Connection-level settings
    /// (timeouts, TLS, BacklogPolicy) are configured separately, via the
    /// <see cref="StackExchange.Redis.ConfigurationOptions"/> callback on AddCacheWeaveRedis.
    /// </summary>
    public sealed class RedisCacheOptions
    {
        /// <summary>
        /// How long an eviction waits for the multiplexer to reconnect before giving up, so a delete
        /// issued during a failover still lands rather than leaving a stale entry served until it expires.
        /// Reads and writes are unaffected — they always fail fast, since a dropped one is just a cache miss.
        /// Set to <see cref="TimeSpan.Zero"/> to disable the retry entirely. Default: 1.5 seconds.
        /// </summary>
        public TimeSpan EvictionRetryWindow { get; set; } = TimeSpan.FromSeconds(1.5);

        /// <summary>
        /// How often the multiplexer is polled for a restored connection while inside
        /// <see cref="EvictionRetryWindow"/>. Default: 50 milliseconds.
        /// </summary>
        public TimeSpan ReconnectPollInterval { get; set; } = TimeSpan.FromMilliseconds(50);

        /// <summary>
        /// Page size for the SCAN cursor and the batch size for the DEL calls used by
        /// prefix eviction. Default: 250.
        /// </summary>
        public int ScanPageSize { get; set; } = 250;
    }
}
