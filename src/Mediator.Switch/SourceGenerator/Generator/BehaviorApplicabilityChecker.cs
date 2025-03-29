using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Generator;

public static class BehaviorApplicabilityChecker
{
    public static bool IsApplicable(
        Compilation compilation,
        (ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters) behavior,
        ITypeSymbol requestType,
        ITypeSymbol responseType)
    {
        return ConstraintChecker.IsConstraintSatisfied(compilation, behavior.TypeParameters[0], requestType) &&
               ConstraintChecker.IsConstraintSatisfied(compilation, behavior.TypeParameters[1], responseType);
    }
}