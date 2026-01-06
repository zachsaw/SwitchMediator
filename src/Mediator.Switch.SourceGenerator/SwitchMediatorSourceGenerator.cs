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
    }
}