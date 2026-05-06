namespace CacheWeave.Faster;

public sealed class FasterCacheOptions
{
    /// <summary>Directory path for FASTER log files. Defaults to a temp subdirectory.</summary>
    public string LogDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "cacheweave-faster");

    /// <summary>In-memory log size in bytes. Defaults to 128MB.</summary>
    public long MemorySizeBytes { get; set; } = 128L * 1024 * 1024;

    /// <summary>Page size in bytes. Defaults to 16MB.</summary>
    public long PageSizeBytes { get; set; } = 16L * 1024 * 1024;
}
