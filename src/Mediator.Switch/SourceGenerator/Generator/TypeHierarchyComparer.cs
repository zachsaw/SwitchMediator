using Mediator.Switch.SourceGenerator.Extensions;
using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Generator;

public class TypeHierarchyComparer : IComparer<ITypeSymbol>
{
    public int Compare(ITypeSymbol? x, ITypeSymbol? y)
    {
        if (SymbolEqualityComparer.Default.Equals(x, y)) return 0;
        if (x == null) return 1;
        if (y == null) return -1;

        if (x.IsDerivedFrom(y)) return -1;

        if (y.IsDerivedFrom(x)) return 1;

        // If neither derives from the other, maintain original order
        return string.Compare(x.ToString(), y.ToString(), StringComparison.Ordinal);
    }
}