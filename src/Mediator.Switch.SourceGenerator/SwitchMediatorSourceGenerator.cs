using Mediator.Switch.SourceGenerator.Exceptions;
using Mediator.Switch.SourceGenerator.Generator;
using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator
{
    [Generator]
    public class SwitchMediatorSourceGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new SyntaxCollector());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxReceiver is not SyntaxCollector receiver)
                return;

            try
            {
                var cancellationToken = context.CancellationToken;
                var analyzer = new SemanticAnalyzer(context.Compilation);
                var analysis = analyzer.Analyze(receiver.Types, cancellationToken);
                if (analysis.MediatorClass == null)
                    return;

                cancellationToken.ThrowIfCancellationRequested();

                ReportMissingHandlers(context, analysis);

                var sourceCode = CodeGenerator.Generate(analysis);

                context.AddSource($"{analysis.MediatorClass.Name}.g.cs", sourceCode);
            }
            catch (InvalidOperationException ex)
            {
                // Handle cases where required symbols are not found
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "SMG001",
                        "Required Mediator.Switch types not found",
                        ex.Message,
                        "Mediator.Switch.Generation",
                        DiagnosticSeverity.Error,
                        true),
                    Location.None));
            }
            catch (SourceGenerationException ex)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "SMG002",
                        "Error generating SwitchMediator",
                        ex.Message,
                        "Mediator.Switch.Generation",
                        DiagnosticSeverity.Error,
                        true),
                    ex.Location));
            }
            catch (Exception ex)
            {
                // General error handling during generation
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "SMG999",
                        "BUG in SwitchMediator (please report to developer)",
                        ex.ToString(),
                        "Mediator.Switch.Generation",
                        DiagnosticSeverity.Error,
                        true),
                    Location.None));
            }
        }

        private static readonly DiagnosticDescriptor MissingHandlerRule = new(
            "SMG003",
            "Request has no handler",
            "Request '{0}' has no handler, so sending it throws at runtime. Add a handler, or mark the request with [AllowMissingHandler] if it is never sent.",
            "Mediator.Switch.Generation",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// Reports every request the generated dispatcher cannot route, using the same resolution as
        /// the switch (base types included), so the diagnostic matches what Send would do at runtime.
        /// </summary>
        private static void ReportMissingHandlers(GeneratorExecutionContext context, SemanticAnalysis analysis)
        {
            foreach (var request in analysis.RequestBehaviors)
            {
                var requestType = request.Request.Class;

                if (requestType.IsAbstract || requestType.IsUnboundGenericType || requestType.TypeParameters.Length > 0)
                {
                    continue;
                }

                if (AllowsMissingHandler(requestType))
                {
                    continue;
                }

                if (CodeGenerator.TryGetActualRequest(analysis.RequestSymbol, analysis.Handlers, request.Request) is not null)
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    MissingHandlerRule,
                    requestType.Locations.FirstOrDefault() ?? Location.None,
                    requestType.Name));
            }
        }

        private static bool AllowsMissingHandler(INamedTypeSymbol requestType) =>
            requestType.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == "Mediator.Switch.AllowMissingHandlerAttribute");
    }
}
