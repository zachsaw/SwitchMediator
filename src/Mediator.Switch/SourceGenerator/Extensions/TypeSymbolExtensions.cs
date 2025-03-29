using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Extensions;

public static class TypeSymbolExtensions
{
    public static bool IsDerivedFrom(this ITypeSymbol type, ITypeSymbol baseType)
    {
        var current = type;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
            current = current.BaseType;
        }
        return false;
    }
    
    public static bool IsNullableValueType(this ITypeSymbol typeSymbol)
    {
        return typeSymbol.IsValueType &&
               typeSymbol is INamedTypeSymbol { IsGenericType: true } namedType &&
               SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition,
                   namedType.ContainingAssembly?.GetTypeByMetadataName("System.Nullable`1"));
    }
}