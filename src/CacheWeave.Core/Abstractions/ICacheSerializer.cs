namespace CacheWeave.Core.Abstractions;

/// <summary>
/// Abstracts serialization and deserialization of cached values.
/// Implement this interface to swap out System.Text.Json for Newtonsoft.Json,
/// MessagePack, or any other serializer.
/// </summary>
public interface ICacheSerializer
{
    /// <summary>Serializes <paramref name="value"/> to a string for storage.</summary>
    string Serialize<T>(T value);

    /// <summary>Deserializes a stored string back to <typeparamref name="T"/>.</summary>
    T? Deserialize<T>(string value);

    /// <summary>
    /// Serializes <paramref name="value"/> to a string for storage.
    /// Used when the concrete type is only known at runtime.
    /// </summary>
    string Serialize(object value, Type type);

    /// <summary>
    /// Deserializes a stored string back to the given <paramref name="type"/>.
    /// Used when the concrete type is only known at runtime.
    /// </summary>
    object? Deserialize(string value, Type type);
}
