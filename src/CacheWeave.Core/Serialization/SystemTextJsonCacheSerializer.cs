using System.Text.Json;
using CacheWeave.Core.Abstractions;

namespace CacheWeave.Core.Serialization;

/// <summary>
/// Default <see cref="ICacheSerializer"/> implementation using System.Text.Json.
/// Registered automatically unless overridden.
/// </summary>
public sealed class SystemTextJsonCacheSerializer : ICacheSerializer
{
    private readonly JsonSerializerOptions _options;

    public SystemTextJsonCacheSerializer(JsonSerializerOptions? options = null)
    {
        _options = options ?? new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, _options);

    public T? Deserialize<T>(string value) =>
        JsonSerializer.Deserialize<T>(value, _options);

    public string Serialize(object value, Type type) =>
        JsonSerializer.Serialize(value, type, _options);

    public object? Deserialize(string value, Type type) =>
        JsonSerializer.Deserialize(value, type, _options);
}
