using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Generator;

public static class BehaviorApplicabilityChecker
{
    public static bool IsApplicable(Compilation compilation,
        IReadOnlyList<ITypeParameterSymbol> behaviorTypeParameters,
        ITypeSymbol requestType,
        ITypeSymbol responseType)
    {
        return ConstraintChecker.IsConstraintSatisfied(compilation, behaviorTypeParameters[0], requestType) &&
               ConstraintChecker.IsConstraintSatisfied(compilation, behaviorTypeParameters[1], responseType);
    }
}