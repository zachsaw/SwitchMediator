using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Extensions;

public static class SymbolExtensions
{
    public static string GetVariableName(this ISymbol s) =>
        s.ToString().DropGenerics().ToVariableName().ToLowerFirst();
}