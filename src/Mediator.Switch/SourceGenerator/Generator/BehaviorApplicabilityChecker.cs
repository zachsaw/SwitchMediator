using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Generator;

public static class BehaviorApplicabilityChecker
{
    public static bool IsApplicable(
        (ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters) behavior,
        ITypeSymbol requestType,
        ITypeSymbol responseType)
    {
        return ConstraintChecker.IsConstraintSatisfied(behavior.TypeParameters[0], requestType) &&
               ConstraintChecker.IsConstraintSatisfied(behavior.TypeParameters[1], responseType);
    }
}