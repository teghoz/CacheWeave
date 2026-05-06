namespace CacheWeave.SQLite;

public sealed class SQLiteCacheOptions
{
    /// <summary>Path to the SQLite database file. Defaults to a local cache.db.</summary>
    public string DatabasePath { get; set; } = "cacheweave.db";

    /// <summary>SQLite table name for cache entries.</summary>
    public string TableName { get; set; } = "CacheEntries";
}
