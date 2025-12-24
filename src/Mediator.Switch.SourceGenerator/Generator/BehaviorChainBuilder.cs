using Mediator.Switch.SourceGenerator.Extensions;
using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Generator;

public static class BehaviorChainBuilder
{
    public static string BuildRequest(List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> behaviors,
        string requestName,
        string coreHandler)
    {
        var chain = $"/* Request Handler */ {coreHandler}";
        return behaviors.Any()
            ? behaviors
                .Aggregate(
                    seed: chain,
                    func: (innerChain, behavior) =>
                        $"{behavior.Class.GetVariableName()}__{requestName}.Handle(request, ct => \n            {innerChain.Replace("cancellationToken", "ct")},\n            cancellationToken)"
                )
            : chain;
    }

    public static string BuildNotification(
        List<(INamedTypeSymbol Class, ITypeSymbol TNotification, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> behaviors,
        string notificationName,
        string notificationType,
        string coreHandler)
    {
        var chain = $"/* Notification Handler */ {coreHandler}";

        return behaviors.Any()
            ? behaviors.Aggregate(
                seed: chain,
                func: (innerChain, behavior) =>
                    $"{behavior.Class.GetVariableName()}__{notificationName}.Handle(({notificationType}) notification, ct => \n            {innerChain.Replace("cancellationToken", "ct")},\n            cancellationToken)"
            )
            : chain;
    }
}