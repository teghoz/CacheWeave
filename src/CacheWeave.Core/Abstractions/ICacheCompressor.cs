namespace CacheWeave.Core.Abstractions;

/// <summary>
/// Compresses and decompresses cache values before storage and after retrieval.
/// Implement this interface to swap compression algorithms.
/// The default implementation uses GZip.
/// </summary>
public interface ICacheCompressor
{
    /// <summary>Compresses a UTF-8 string to a Base64-encoded compressed string.</summary>
    string Compress(string value);

    /// <summary>Decompresses a Base64-encoded compressed string back to a UTF-8 string.</summary>
    string Decompress(string value);
}
