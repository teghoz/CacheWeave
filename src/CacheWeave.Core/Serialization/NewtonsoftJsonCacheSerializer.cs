using CacheWeave.Core.Abstractions;
using Newtonsoft.Json;

namespace CacheWeave.Core.Serialization;

/// <summary>
/// <see cref="ICacheSerializer"/> implementation backed by <c>Newtonsoft.Json</c>.
/// Register via <c>AddCacheWeave(o => o.Serializer = CacheWeaveSerializerType.NewtonsoftJson)</c>.
/// </summary>
public sealed class NewtonsoftJsonCacheSerializer : ICacheSerializer
{
    private readonly JsonSerializerSettings _settings;

    /// <summary>Initialises the serializer with default settings.</summary>
    public NewtonsoftJsonCacheSerializer() : this(DefaultSettings()) { }

    /// <summary>Initialises the serializer with custom <see cref="JsonSerializerSettings"/>.</summary>
    public NewtonsoftJsonCacheSerializer(JsonSerializerSettings settings)
        => _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    /// <inheritdoc />
    public string Serialize<T>(T value)
        => JsonConvert.SerializeObject(value, _settings);

    /// <inheritdoc />
    public T? Deserialize<T>(string value)
        => JsonConvert.DeserializeObject<T>(value, _settings);

    /// <inheritdoc />
    public string Serialize(object value, Type type)
        => JsonConvert.SerializeObject(value, type, _settings);

    /// <inheritdoc />
    public object? Deserialize(string value, Type type)
        => JsonConvert.DeserializeObject(value, type, _settings);

    private static JsonSerializerSettings DefaultSettings() => new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.DateTimeOffset,
        Formatting = Formatting.None,
        // Prevents JsonSerializationException on object graphs with circular references
        // (e.g. EF Core entities with navigation properties that are not projected to DTOs).
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
    };
}
