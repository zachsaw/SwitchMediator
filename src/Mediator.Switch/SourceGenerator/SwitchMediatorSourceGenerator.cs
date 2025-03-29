using Mediator.Switch.SourceGenerator.Generator;
using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator
{
    [Generator]
    public class SwitchMediatorSourceGenerator : ISourceGenerator
    {
        /*
         * TODO:
         *     - add [RequestHandler(typeof(PingHandler))] attribute
         *     - throw fine-grained custom exceptions with location info, report with it too.
         *     - support CancellationToken in mediator pipelines / handlers. (check MediatR)
         */
        
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
                var analyzer = new SemanticAnalyzer(context.Compilation);
                var (handlers, requestBehaviors, notifications) = analyzer.Analyze(receiver.Classes);

                var sourceCode = CodeGenerator.Generate(handlers, requestBehaviors, notifications);

                context.AddSource("SwitchMediator.g.cs", sourceCode);
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
            catch (Exception ex)
            {
                // General error handling during generation
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "SMG999",
                        "Error generating SwitchMediator",
                        ex.ToString(),
                        "Mediator.Switch.Generation",
                        DiagnosticSeverity.Error,
                        true),
                    Location.None));
            }
        }
    }
}