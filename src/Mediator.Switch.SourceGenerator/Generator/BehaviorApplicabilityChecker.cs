using Mediator.Switch.SourceGenerator.Exceptions;
using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Generator;

public static class BehaviorApplicabilityChecker
{
    public static bool IsApplicable(
        Compilation compilation,
        ITypeSymbol behaviorClass,
        IReadOnlyList<ITypeParameterSymbol> behaviorTypeParameters,
        ITypeSymbol requestType,
        ITypeSymbol responseType)
    {
        if (behaviorTypeParameters.Count != 2)
        {
            throw new SourceGenerationException(
                "Closed behaviors are not supported in SwitchMediator. Use constraints on generic type parameters instead.",
                behaviorClass.Locations[0]);
        }

        var substitutions = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default)
        {
            [behaviorTypeParameters[0]] = requestType,
            [behaviorTypeParameters[1]] = responseType
        };

        return ConstraintChecker.IsConstraintSatisfied(compilation, behaviorTypeParameters[0], requestType, substitutions) &&
               ConstraintChecker.IsConstraintSatisfied(compilation, behaviorTypeParameters[1], responseType, substitutions);
    }

    public static bool IsApplicable(
        Compilation compilation,
        ITypeSymbol behaviorClass,
        IReadOnlyList<ITypeParameterSymbol> behaviorTypeParameters,
        ITypeSymbol notificationType)
    {
        if (behaviorTypeParameters.Count != 1)
        {
            throw new SourceGenerationException(
                "Closed notification behaviors are not supported in SwitchMediator. Use constraints on generic type parameters instead.",
                behaviorClass.Locations[0]);
        }

        var substitutions = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default)
        {
            [behaviorTypeParameters[0]] = notificationType
        };

        return ConstraintChecker.IsConstraintSatisfied(compilation, behaviorTypeParameters[0], notificationType, substitutions);
    }
}
