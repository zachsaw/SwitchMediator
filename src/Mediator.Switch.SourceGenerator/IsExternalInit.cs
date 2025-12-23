// Compatibility shim for record types when targeting older frameworks like netstandard2.0
// This defines the required IsExternalInit type used by the compiler.
// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
    // The compiler only needs the existence of this type; make it internal to avoid API surface.
    // ReSharper disable once UnusedType.Global
    internal static class IsExternalInit { }
}

