using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Mediator.Switch.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class RequestHandlerAttributeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SMD001";
    private const string Category = "Design";

    private static readonly LocalizableString _title = "Invalid RequestHandlerAttribute";
    private static readonly LocalizableString _messageFormat = "The handler '{0}' specified in RequestHandlerAttribute does not implement IRequestHandler<{1}, ...>";
    private static readonly LocalizableString _description = "The type specified in RequestHandlerAttribute must implement IRequestHandler<TRequest, TResponse> where TRequest is the class the attribute is applied to.";

    private static readonly DiagnosticDescriptor _rule = new(
        DiagnosticId,
        _title,
        _messageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(_rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.NamedType);
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context)
    {
        var requestType = (INamedTypeSymbol)context.Symbol;
        var compilation = context.Compilation;

        // Resolve required types
        var requestHandlerAttributeSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.RequestHandlerAttribute");
        var iRequestHandlerSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.IRequestHandler`2");

        if (requestHandlerAttributeSymbol == null || iRequestHandlerSymbol == null) return;

        foreach (var attribute in requestType.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, requestHandlerAttributeSymbol))
            {
                if (attribute.ConstructorArguments.Length == 1 &&
                    attribute.ConstructorArguments[0].Value is INamedTypeSymbol handlerType)
                {
                    var implementsInterface = handlerType.AllInterfaces.Any(i =>
                        SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, iRequestHandlerSymbol) &&
                        i.TypeArguments.Length == 2 &&
                        SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], requestType));

                    if (!implementsInterface)
                    {
                        var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? requestType.Locations[0];

                        var diagnostic = Diagnostic.Create(
                            _rule,
                            location,
                            handlerType.Name,  // {0}
                            requestType.Name); // {1}

                        context.ReportDiagnostic(diagnostic);
                    }
                }
            }
        }
    }
}