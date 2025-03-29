using Mediator.Switch.SourceGenerator.Extensions;
using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Generator;

public static class BehaviorChainBuilder
{
    public static string Build(List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> behaviors, string requestName, string coreHandler)
    {
        var chain = $"await {coreHandler}(request)";
        return behaviors.Any()
            ? behaviors
                .Aggregate(
                    seed: chain,
                    func: (innerChain, behavior) =>
                        $"await _{behavior.Class.GetVariableName()}__{requestName}.Handle(request, async () => \n            {innerChain})"
                )
            : chain;
    }
}