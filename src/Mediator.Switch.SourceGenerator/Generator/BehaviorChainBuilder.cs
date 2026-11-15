using Mediator.Switch.SourceGenerator.Extensions;
using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Generator;

public static class BehaviorChainBuilder
{
    /// <summary>
    /// Builds the pipeline for <see cref="Mediator.Switch.IValueContextualPipelineBehavior{TRequest, TResponse}"/>:
    /// every step receives the request as a parameter (r{i}, c{i}) instead of capturing it, so the
    /// whole chain can be composed once and reused for every send.
    /// </summary>
    public static string BuildContextualRequest(
        List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters, bool IsValueTask)> behaviors,
        string requestName,
        string handlerVariable)
    {
        var chain = $"{handlerVariable}.Handle(r{behaviors.Count}, c{behaviors.Count})";

        for (var index = behaviors.Count - 1; index >= 0; index--)
        {
            var behavior = behaviors[index];
            chain = $"{behavior.Class.GetVariableName()}__{requestName}.Handle(r{index}, (r{index + 1}, c{index + 1}) =>\n            {chain}, c{index})";
        }

        return chain;
    }

    public static string BuildRequest(List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters, bool IsValueTask)> behaviors,
        string requestName,
        string coreHandler,
        Func<INamedTypeSymbol, bool>? isContextual = null)
    {
        var chain = $"/* Request Handler */ {coreHandler}";
        if (!behaviors.Any())
        {
            return chain;
        }

        return behaviors.Aggregate(
            seed: chain,
            func: (innerChain, behavior) =>
            {
                // A contextual behavior expects the request on its next delegate: the per-call chain
                // keeps capturing it, so the adapter simply ignores the parameter.
                var nextParameter = isContextual?.Invoke(behavior.Class) == true ? "(_, ct)" : "ct";

                return $"{behavior.Class.GetVariableName()}__{requestName}.Handle(request, {nextParameter} =>\n            {innerChain.Replace("cancellationToken", "ct")},\n            cancellationToken)";
            });
    }

    public static string BuildNotification(
        List<(INamedTypeSymbol Class, ITypeSymbol TNotification, IReadOnlyList<ITypeParameterSymbol> TypeParameters, bool IsValueTask)> behaviors,
        string notificationName,
        string notificationVariableName,
        string coreHandler)
    {
        var chain = $"/* Notification Handler */ {coreHandler}";

        return behaviors.Any()
            ? behaviors.Aggregate(
                seed: chain,
                func: (innerChain, behavior) =>
                    $"{behavior.Class.GetVariableName()}__{notificationName}.Handle({notificationVariableName}, ct =>\n                            {innerChain.Replace("cancellationToken", "ct")},\n                            cancellationToken)"
            )
            : chain;
    }
}