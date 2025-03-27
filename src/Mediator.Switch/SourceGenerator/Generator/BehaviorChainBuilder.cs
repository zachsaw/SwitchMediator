using Mediator.Switch.SourceGenerator.Extensions;
using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Generator;

public static class BehaviorChainBuilder
{
    public static string Build(List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> behaviors, string requestName, string coreHandler)
    {
        if (!behaviors.Any()) return $"await {coreHandler}(request)";
        if (behaviors.Count == 1) return $"await _{behaviors.Single().Class.GetVariableName()}__{requestName}.Handle(request, async req => \n            await {coreHandler}(req))";

        var chain = coreHandler;
        behaviors.Reverse(); // Outer-to-inner
        var first = behaviors.First();
        var last = behaviors.Last();
        var reqCount = behaviors.Count;
        
        chain = $"await _{first.Class.GetVariableName()}__{requestName}.Handle(req{reqCount - 1}, async req{reqCount} => \n            await {chain}(req{reqCount}))";
        reqCount--;
        
        foreach (var behavior in behaviors.Skip(1).Take(Math.Max(0, behaviors.Count - 2)))
        {
            chain = $"await _{behavior.Class.GetVariableName()}__{requestName}.Handle(req{reqCount - 1}, async req{reqCount} => \n            {chain})";
            reqCount--;
        }
        
        chain = $"await _{last.Class.GetVariableName()}__{requestName}.Handle(request, async req{reqCount} => \n            {chain})";
        return chain;
    }
}