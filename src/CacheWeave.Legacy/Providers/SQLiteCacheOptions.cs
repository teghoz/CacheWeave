using System.IO;

namespace CacheWeave.Legacy.Providers
{
    public sealed class SQLiteCacheOptions
    {
        public string DatabasePath { get; set; } = "cacheweave.db";
        public string TableName { get; set; } = "CacheEntries";
    }
}
