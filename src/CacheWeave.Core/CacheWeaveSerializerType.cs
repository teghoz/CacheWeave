namespace CacheWeave.Core;

/// <summary>
/// Selects the JSON serializer used by CacheWeave to serialize and deserialize cached values.
/// </summary>
public enum CacheWeaveSerializerType
{
    /// <summary>
    /// Use <c>System.Text.Json</c> (default). Zero additional dependencies.
    /// </summary>
    SystemTextJson,

    /// <summary>
    /// Use <c>Newtonsoft.Json</c>. Requires <c>Newtonsoft.Json</c> to be present in the host project.
    /// Prefer this when the rest of the application already uses Newtonsoft, or when you need
    /// features not available in STJ (e.g. polymorphic serialization without source generators).
    /// </summary>
    NewtonsoftJson
}
