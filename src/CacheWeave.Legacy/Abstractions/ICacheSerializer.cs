using System;
using System.Diagnostics.CodeAnalysis;

namespace CacheWeave.Legacy.Abstractions
{
    public interface ICacheSerializer
    {
        string Serialize<T>(T value);

        [return: MaybeNull]
        T Deserialize<T>(string value);

        string Serialize(object value, Type type);
        object? Deserialize(string value, Type type);
    }
}
