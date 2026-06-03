namespace CacheWeave.Legacy.Abstractions
{
    public interface ICacheCompressor
    {
        string Compress(string value);
        string Decompress(string value);
    }
}
