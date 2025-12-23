using Mediator.Switch.SourceGenerator.Extensions;
using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Generator;

public static class BehaviorChainBuilder
{
    public static string Build(List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> behaviors, string requestName, string coreHandler)
    {
        var chain = $"/* Request Handler */ {coreHandler}(request, cancellationToken)";
        return behaviors.Any()
            ? behaviors
                .Aggregate(
                    seed: chain,
                    func: (innerChain, behavior) =>
                        $"{behavior.Class.GetVariableName()}__{requestName}.Handle(request, () => \n            {innerChain},\n            cancellationToken)"
                )
            : chain;
    }

    public static string BuildNotification(
        List<(INamedTypeSymbol Class, ITypeSymbol TNotification, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> behaviors,
        string notificationName,
        string coreHandler)
    {
        var chain = $"/* Notification Handler */ await {coreHandler}";

        return behaviors.Any()
            ? behaviors.Aggregate(
                seed: chain,
                func: (innerChain, behavior) =>
                    $"await {behavior.Class.GetVariableName()}__{notificationName}.Handle(({notificationName})notification, async (ct) => {{ {innerChain.Replace("cancellationToken", "ct")}; }}, cancellationToken)"
            )
            : chain;
    }
}