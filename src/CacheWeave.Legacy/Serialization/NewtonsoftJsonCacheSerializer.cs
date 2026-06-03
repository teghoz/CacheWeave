using System;
using System.Diagnostics.CodeAnalysis;
using CacheWeave.Legacy.Abstractions;
using Newtonsoft.Json;

namespace CacheWeave.Legacy.Serialization
{
    public sealed class NewtonsoftJsonCacheSerializer : ICacheSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DateParseHandling = DateParseHandling.DateTimeOffset,
            Formatting = Formatting.None,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };

        public string Serialize<T>(T value) =>
            JsonConvert.SerializeObject(value, Settings);

        [return: MaybeNull]
        public T Deserialize<T>(string value) =>
            JsonConvert.DeserializeObject<T>(value, Settings);

        public string Serialize(object value, Type type) =>
            JsonConvert.SerializeObject(value, type, Settings);

        public object? Deserialize(string value, Type type) =>
            JsonConvert.DeserializeObject(value, type, Settings);
    }
}
