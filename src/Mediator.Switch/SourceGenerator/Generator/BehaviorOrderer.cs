using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Generator;

public static class BehaviorOrderer
{
    public static List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> Order(
        List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> behaviors,
        INamedTypeSymbol orderAttributeSymbol)
    {
        return behaviors
            .Select(behavior =>
            {
                var orderAttribute = behavior.Class.GetAttributes()
                    .FirstOrDefault(attr => attr.AttributeClass?.Equals(orderAttributeSymbol, SymbolEqualityComparer.Default) ?? false);

                var order = orderAttribute?.ConstructorArguments.FirstOrDefault().Value as int? ?? int.MaxValue;
                return (Behavior: behavior, Order: order);
            })
            .OrderBy(b => b.Order)
            .Select(b => b.Behavior)
            .ToList();
    }
}