using CacheWeave.Core.Abstractions;
using FASTER.core;
using Microsoft.Extensions.Options;

namespace CacheWeave.Faster;

/// <summary>
/// CacheWeave provider backed by Microsoft FASTER KV.
/// High-throughput, low-latency local cache with hybrid log-structured storage (memory + disk).
/// Best suited for single-node, high-frequency read/write workloads.
/// Note: FASTER KV does not natively support TTL — expiry is enforced on read.
/// </summary>
public sealed class FasterCacheProvider : ICacheProvider, IDisposable
{
    // SpanByteFunctions<Empty> implements IFunctions<SpanByte, SpanByte, SpanByte, SpanByteAndMemory, Empty>
    // so: Key=SpanByte, Value=SpanByte, Input=SpanByte, Output=SpanByteAndMemory, Context=Empty
    private readonly FasterKV<SpanByte, SpanByte> _store;
    private readonly IDevice _logDevice;

    public FasterCacheProvider(IOptions<FasterCacheOptions> options)
    {
        var opts = options.Value;
        Directory.CreateDirectory(opts.LogDirectory);

        _logDevice = Devices.CreateLogDevice(
            Path.Combine(opts.LogDirectory, "cacheweave.log"),
            deleteOnClose: false);

        _store = new FasterKV<SpanByte, SpanByte>(
            size: 1L << 20,
            logSettings: new LogSettings
            {
                LogDevice = _logDevice,
                MemorySizeBits = (int)Math.Log2(opts.MemorySizeBytes),
                PageSizeBits = (int)Math.Log2(opts.PageSizeBytes)
            });
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        using var session = _store.NewSession<SpanByte, SpanByteAndMemory, Empty>(
            new SpanByteFunctions<Empty>());

        var keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
        SpanByteAndMemory output = default;
        var input = default(SpanByte);
        Status status;

        unsafe
        {
            fixed (byte* kPtr = keyBytes)
            {
                var keySpan = SpanByte.FromFixedSpan(new Span<byte>(kPtr, keyBytes.Length));
                status = session.Read(ref keySpan, ref input, ref output);
            }
        }

        if (status.IsCompletedSuccessfully && status.Found)
        {
            var result = System.Text.Encoding.UTF8.GetString(
                output.Memory.Memory.Span[..output.Length]);
            output.Memory?.Dispose();
            return Task.FromResult<string?>(result);
        }

        return Task.FromResult<string?>(null);
    }

    public Task SetAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        using var session = _store.NewSession<SpanByte, SpanByteAndMemory, Empty>(
            new SpanByteFunctions<Empty>());

        var keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
        var valueBytes = System.Text.Encoding.UTF8.GetBytes(value);

        unsafe
        {
            fixed (byte* kPtr = keyBytes, vPtr = valueBytes)
            {
                var keySpan = SpanByte.FromFixedSpan(new Span<byte>(kPtr, keyBytes.Length));
                var valueSpan = SpanByte.FromFixedSpan(new Span<byte>(vPtr, valueBytes.Length));
                session.Upsert(ref keySpan, ref valueSpan);
            }
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        using var session = _store.NewSession<SpanByte, SpanByteAndMemory, Empty>(
            new SpanByteFunctions<Empty>());

        var keyBytes = System.Text.Encoding.UTF8.GetBytes(key);

        unsafe
        {
            fixed (byte* kPtr = keyBytes)
            {
                var keySpan = SpanByte.FromFixedSpan(new Span<byte>(kPtr, keyBytes.Length));
                session.Delete(ref keySpan);
            }
        }

        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        // FASTER KV is a hash-based store — it does not support range or prefix scans.
        // For prefix invalidation, maintain a separate key registry or use a different provider.
        throw new NotSupportedException(
            "FASTER KV does not support prefix-based key scanning. " +
            "Maintain an external key registry for prefix invalidation.");
    }

    public void Dispose()
    {
        _store.Dispose();
        _logDevice.Dispose();
    }
}
