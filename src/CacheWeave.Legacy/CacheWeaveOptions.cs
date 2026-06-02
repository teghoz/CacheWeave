using System;

namespace CacheWeave.Legacy
{
    public sealed class CacheWeaveOptions
    {
        /// <summary>When false, all cache operations are no-ops.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Separator used between cache key segments. Default is ":".</summary>
        public string KeySeparator { get; set; } = ":";

        /// <summary>Optional prefix prepended to every cache key.</summary>
        public string? GlobalKeyPrefix { get; set; }

        /// <summary>Optional version segment injected into every cache key.</summary>
        public string? KeyVersion { get; set; }

        /// <summary>Default TTL applied when no expiry is specified. Default is 300 seconds.</summary>
        public TimeSpan? DefaultExpiry { get; set; } = TimeSpan.FromSeconds(300);

        /// <summary>Serializer to use for cache values. Default is System.Text.Json.</summary>
        public CacheWeaveSerializerType Serializer { get; set; } = CacheWeaveSerializerType.SystemTextJson;

        /// <summary>When true, wraps the provider with GZip compression.</summary>
        public bool EnableCompression { get; set; } = false;
    }
}
