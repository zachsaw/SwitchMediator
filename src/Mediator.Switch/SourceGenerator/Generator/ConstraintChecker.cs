using Mediator.Switch.SourceGenerator.Extensions;
using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Generator;

public static class ConstraintChecker
{
    public static bool IsConstraintSatisfied(ITypeParameterSymbol? param, ITypeSymbol typeSymbol)
    {
        if (param == null) return true; // No constraints

        var constraints = param.ConstraintTypes;
        if (!constraints.Any()) return true; // No constraints

        foreach (var constraint in constraints.Where(c => c != null))
        {
            // Check if requestType implements or inherits the constraint
            if (!typeSymbol.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, constraint)) &&
                !typeSymbol.IsDerivedFrom(constraint))
            {
                return false;
            }
        }

        return true;
    }
}