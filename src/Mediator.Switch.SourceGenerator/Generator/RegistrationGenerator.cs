using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Generator;

/// <summary>
/// Writes the mediator's registrations: every component it resolves, with a factory that calls the component's
/// constructor wherever the generated code can, so a container needs no reflection to create it. The mediator's
/// static constructor hands the method that passes them to an <c>ISwitchMediatorRegistrar</c> to
/// <c>SwitchMediatorRegistrations</c>, where container extensions find it.
/// Only <c>System.IServiceProvider</c> is used, so this works with any container.
/// </summary>
internal static class RegistrationGenerator
{
    private const string Resolve = "SwitchMediatorResolve";

    public static string Generate(
        SemanticAnalysis analysis,
        string className,
        IEnumerable<INamedTypeSymbol> requestHandlers,
        IEnumerable<(ITypeSymbol Notification, IReadOnlyList<(INamedTypeSymbol Class, bool IsValueTask)> Handlers)> notificationHandlers,
        IEnumerable<INamedTypeSymbol?> behaviors,
        SymbolDisplayFormat fqn)
    {
        var mediator = analysis.MediatorClass!;
        // A static constructor the application declares itself cannot be joined; its extension falls back to KnownTypes.
        if (mediator.StaticConstructors.Any(c => !c.IsImplicitlyDeclared))
        {
            return string.Empty;
        }

        var assembly = mediator.ContainingAssembly;
        var entries = new List<string>();
        var mediatorFactory = analysis.ServiceProviderMode == MediatorServiceProviderMode.Direct
            ? $"sp => new {className}(sp)"
            : $"sp => new {className}({Resolve}<global::Mediator.Switch.ISwitchMediatorServiceProvider>(sp))";
        entries.Add($"registrar.Add(typeof(global::Mediator.Switch.IMediator), typeof({className}), {mediatorFactory}, null);");
        foreach (var forwarded in new[] { "ISender", "IPublisher", "IValueMediator", "IValueSender", "IValuePublisher" })
        {
            entries.Add($"registrar.Add(typeof(global::Mediator.Switch.{forwarded}), typeof({className}), sp => {Resolve}<global::Mediator.Switch.IMediator>(sp), null);");
        }
        foreach (var handler in requestHandlers)
        {
            entries.Add(Entry(handler.ToDisplayString(fqn), handler, null, assembly, fqn));
        }
        foreach (var (notification, handlers) in notificationHandlers)
        {
            foreach (var (handler, isValueTask) in handlers)
            {
                var service = $"global::Mediator.Switch.{(isValueTask ? "IValueNotificationHandler" : "INotificationHandler")}<{notification.ToDisplayString(fqn)}>";
                entries.Add(Entry(service, handler, notification.ToDisplayString(fqn), assembly, fqn));
            }
        }
        foreach (var behavior in behaviors)
        {
            if (behavior != null)
            {
                entries.Add(Entry(behavior.ToDisplayString(fqn), behavior, null, assembly, fqn));
            }
        }

        return $$"""


                static {{className}}()
                {
                    global::Mediator.Switch.SwitchMediatorRegistrations.Add(typeof({{className}}), RegisterSwitchMediatorComponents);
                }

                private static void RegisterSwitchMediatorComponents(global::Mediator.Switch.ISwitchMediatorRegistrar registrar)
                {
                    {{string.Join("\n        ", entries)}}
                }

                private static T {{Resolve}}<T>(global::System.IServiceProvider sp) where T : notnull
                {
                    return (T)(sp.GetService(typeof(T)) ?? throw new global::System.InvalidOperationException($"No service for type '{typeof(T)}' has been registered."));
                }
            """;
    }

    // A registration whose factory calls the component's one public constructor, or with no factory (the container
    // then creates it by type) where that constructor is missing, ambiguous or not accessible to the generated code.
    private static string Entry(string service, INamedTypeSymbol implementation, string? notification, IAssemblySymbol assembly, SymbolDisplayFormat fqn)
    {
        var type = implementation.ToDisplayString(fqn);
        var constructors = implementation.InstanceConstructors.Where(c => c.DeclaredAccessibility == Accessibility.Public).ToList();
        var arguments = constructors.Count == 1 && Accessible(implementation, assembly) && Accessible(constructors[0], assembly)
            ? constructors[0].Parameters.Select(p => Argument(p, assembly, fqn)).ToList()
            : null;
        var factory = arguments == null || arguments.Any(a => a == null) ? "null" : $"sp => new {type}({string.Join(", ", arguments)})";
        var notificationArgument = notification == null ? "null" : $"typeof({notification})";
        return $"registrar.Add(typeof({service}), typeof({type}), {factory}, {notificationArgument});";
    }

    // How the container would supply one constructor argument, or null where a factory cannot match it.
    private static string? Argument(IParameterSymbol parameter, IAssemblySymbol assembly, SymbolDisplayFormat fqn)
    {
        if (!Accessible(parameter.Type, assembly) ||
            parameter.GetAttributes().Any(a => a.AttributeClass?.Name is "FromKeyedServicesAttribute" or "ServiceKeyAttribute"))
        {
            return null;
        }
        var type = parameter.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(fqn);
        if (parameter.HasExplicitDefaultValue)
        {
            // The container passes the default when the service is missing; only a null default is matched here.
            return parameter.ExplicitDefaultValue == null && !parameter.Type.IsValueType
                ? $"({type}?)sp.GetService(typeof({type}))"
                : null;
        }
        // IEnumerable<T> resolves as a service in the containers that support multiple registrations.
        return $"{Resolve}<{type}>(sp)";
    }

    // Whether generated code in the mediator's assembly can name the symbol: it and every containing type and type
    // argument are public, or internal to that assembly (or to one that grants it access).
    private static bool Accessible(ISymbol symbol, IAssemblySymbol assembly)
    {
        if (symbol is IArrayTypeSymbol array)
        {
            return Accessible(array.ElementType, assembly);
        }
        if (symbol is ITypeParameterSymbol)
        {
            return true;
        }
        if (symbol is INamedTypeSymbol { IsGenericType: true } generic && !generic.TypeArguments.All(t => Accessible(t, assembly)))
        {
            return false;
        }
        for (var current = symbol; current != null && current is not INamespaceSymbol; current = current.ContainingSymbol)
        {
            switch (current.DeclaredAccessibility)
            {
                case Accessibility.Public:
                case Accessibility.NotApplicable:
                    continue;
                case Accessibility.Internal:
                case Accessibility.ProtectedOrInternal:
                    if (SymbolEqualityComparer.Default.Equals(current.ContainingAssembly, assembly) ||
                        current.ContainingAssembly.GivesAccessTo(assembly))
                    {
                        continue;
                    }
                    return false;
                default:
                    return false;
            }
        }
        return true;
    }
}
