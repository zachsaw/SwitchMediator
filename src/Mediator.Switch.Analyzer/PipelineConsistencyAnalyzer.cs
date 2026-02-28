using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Mediator.Switch.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class PipelineConsistencyAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SMD002";
    private const string Category = "Design";

    private static readonly LocalizableString _title = "Inconsistent pipeline behavior type";
    private static readonly LocalizableString _messageFormat =
        "Pipeline behavior '{0}' implements '{1}' but handler for '{2}' implements '{3}'. All pipeline behaviors must use the same async pattern as the handler.";
    private static readonly LocalizableString _description =
        "If a request handler implements IValueRequestHandler (ValueTask), all applicable pipeline behaviors must implement IValuePipelineBehavior. If the handler implements IRequestHandler (Task), all behaviors must implement IPipelineBehavior.";

    private static readonly DiagnosticDescriptor _rule = new(
        DiagnosticId,
        _title,
        _messageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: _description,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(_rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var compilation = context.Compilation;

        var iRequestHandlerSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.IRequestHandler`2");
        var iValueRequestHandlerSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.IValueRequestHandler`2");
        var iPipelineBehaviorSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.IPipelineBehavior`2");
        var iValuePipelineBehaviorSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.IValuePipelineBehavior`2");

        if (iRequestHandlerSymbol == null || iValueRequestHandlerSymbol == null ||
            iPipelineBehaviorSymbol == null || iValuePipelineBehaviorSymbol == null)
            return;

        // Collect all concrete pipeline behaviors
        var allTypes = GetAllTypes(compilation.GlobalNamespace).ToList();

        var behaviors = allTypes
            .Where(t => !t.IsAbstract && !t.IsUnboundGenericType)
            .SelectMany(t => t.AllInterfaces
                .Where(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, iPipelineBehaviorSymbol) ||
                            SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, iValuePipelineBehaviorSymbol))
                .Select(i => (
                    BehaviorType: t,
                    Interface: i,
                    IsValue: SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, iValuePipelineBehaviorSymbol),
                    TRequest: i.TypeArguments.Length == 2 ? i.TypeArguments[0] : null
                )))
            .Where(b => b.TRequest != null)
            .ToList();

        if (behaviors.Count == 0) return;

        // Find all handlers and check consistency
        foreach (var handlerType in allTypes.Where(t => !t.IsAbstract && !t.IsUnboundGenericType))
        {
            var taskHandlerInterfaces = handlerType.AllInterfaces
                .Where(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, iRequestHandlerSymbol) &&
                            i.TypeArguments.Length == 2)
                .ToList();

            var valueHandlerInterfaces = handlerType.AllInterfaces
                .Where(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, iValueRequestHandlerSymbol) &&
                            i.TypeArguments.Length == 2)
                .ToList();

            // Check Task handlers: all applicable behaviors must be IPipelineBehavior (not IValuePipelineBehavior)
            foreach (var handlerIface in taskHandlerInterfaces)
            {
                var tRequest = handlerIface.TypeArguments[0];
                var applicableValueBehaviors = behaviors
                    .Where(b => b.IsValue && IsApplicable(b.BehaviorType, tRequest))
                    .ToList();

                foreach (var behavior in applicableValueBehaviors)
                {
                    var location = behavior.BehaviorType.Locations.FirstOrDefault() ?? Location.None;
                    context.ReportDiagnostic(Diagnostic.Create(
                        _rule,
                        location,
                        behavior.BehaviorType.Name,
                        "IValuePipelineBehavior",
                        tRequest.Name,
                        "IRequestHandler"));
                }
            }

            // Check ValueTask handlers: all applicable behaviors must be IValuePipelineBehavior (not IPipelineBehavior)
            foreach (var handlerIface in valueHandlerInterfaces)
            {
                var tRequest = handlerIface.TypeArguments[0];
                var applicableTaskBehaviors = behaviors
                    .Where(b => !b.IsValue && IsApplicable(b.BehaviorType, tRequest))
                    .ToList();

                foreach (var behavior in applicableTaskBehaviors)
                {
                    var location = behavior.BehaviorType.Locations.FirstOrDefault() ?? Location.None;
                    context.ReportDiagnostic(Diagnostic.Create(
                        _rule,
                        location,
                        behavior.BehaviorType.Name,
                        "IPipelineBehavior",
                        tRequest.Name,
                        "IValueRequestHandler"));
                }
            }
        }
    }

    private static bool IsApplicable(INamedTypeSymbol behaviorType, ITypeSymbol tRequest)
    {
        if (behaviorType.TypeParameters.Length == 0) return false;

        var typeParam = behaviorType.TypeParameters[0];
        if (typeParam.ConstraintTypes.Length == 0 && !typeParam.HasReferenceTypeConstraint &&
            !typeParam.HasValueTypeConstraint && !typeParam.HasNotNullConstraint)
        {
            return true; // No constraints, applies to all
        }

        foreach (var constraint in typeParam.ConstraintTypes)
        {
            // Check if tRequest is assignable to the constraint type
            if (!IsAssignableTo(tRequest, constraint))
                return false;
        }

        return true;
    }

    private static bool IsAssignableTo(ITypeSymbol type, ITypeSymbol targetType)
    {
        if (SymbolEqualityComparer.Default.Equals(type, targetType)) return true;

        // Check interfaces
        if (type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, targetType) ||
                                         SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, targetType.OriginalDefinition)))
            return true;

        // Check base types
        var current = type.BaseType;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, targetType)) return true;
            current = current.BaseType;
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol ns)
    {
        foreach (var nestedNs in ns.GetNamespaceMembers())
        {
            foreach (var t in GetAllTypes(nestedNs))
                yield return t;
        }

        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in type.GetTypeMembers())
                yield return nested;
        }
    }
}
