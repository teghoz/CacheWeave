using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using CacheWeave.Legacy.Abstractions;

namespace CacheWeave.Legacy.Serialization
{
    public sealed class SystemTextJsonCacheSerializer : ICacheSerializer
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public string Serialize<T>(T value) =>
            JsonSerializer.Serialize(value, Options);

        [return: MaybeNull]
        public T Deserialize<T>(string value) =>
            JsonSerializer.Deserialize<T>(value, Options);

        public string Serialize(object value, Type type) =>
            JsonSerializer.Serialize(value, type, Options);

        public object? Deserialize(string value, Type type) =>
            JsonSerializer.Deserialize(value, type, Options);
    }
}
