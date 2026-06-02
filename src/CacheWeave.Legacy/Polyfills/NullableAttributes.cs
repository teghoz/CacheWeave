// Polyfill for nullable flow attributes that are internal in .NET Framework 4.8's BCL
// but required by C# 8 nullable reference type annotations.
// These are only compiled when targeting net48; on net5+ the BCL provides them.

namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>Specifies that an output may be null even if the corresponding type disallows it.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter |
                    AttributeTargets.Property | AttributeTargets.ReturnValue,
                    Inherited = false)]
    internal sealed class MaybeNullAttribute : Attribute { }

    /// <summary>Specifies that null is allowed as an input even if the corresponding type disallows it.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property,
                    Inherited = false)]
    internal sealed class AllowNullAttribute : Attribute { }

    /// <summary>Specifies that an output will not be null even if the corresponding type allows it.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter |
                    AttributeTargets.Property | AttributeTargets.ReturnValue,
                    Inherited = false)]
    internal sealed class NotNullAttribute : Attribute { }
}
