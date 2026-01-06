using Mediator.Switch.SourceGenerator.Exceptions;
using Mediator.Switch.SourceGenerator.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mediator.Switch.SourceGenerator;

public class SemanticAnalyzer
{
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol _iRequestSymbol;
    private readonly INamedTypeSymbol _iRequestHandlerSymbol;
    private readonly INamedTypeSymbol _iPipelineBehaviorSymbol;
    private readonly INamedTypeSymbol _iNotificationPipelineBehaviorSymbol;
    private readonly INamedTypeSymbol _iNotificationSymbol;
    private readonly INamedTypeSymbol _iNotificationHandlerSymbol;
    private readonly INamedTypeSymbol _orderAttributeSymbol;
    private readonly INamedTypeSymbol _switchMediatorAttributeSymbol;

    public SemanticAnalyzer(Compilation compilation)
    {
        _compilation = compilation;

        _ = compilation.GetTypeByMetadataName("Mediator.Switch.IMediator") ?? throw new InvalidOperationException("Could not find Mediator.Switch.IMediator");
        _ = compilation.GetTypeByMetadataName("Mediator.Switch.ISender") ?? throw new InvalidOperationException("Could not find Mediator.Switch.ISender");
        _ = compilation.GetTypeByMetadataName("Mediator.Switch.IPublisher") ?? throw new InvalidOperationException("Could not find Mediator.Switch.IPublisher");

        _iRequestSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.IRequest`1") ?? throw new InvalidOperationException("Could not find Mediator.Switch.IRequest`1");
        _iRequestHandlerSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.IRequestHandler`2") ?? throw new InvalidOperationException("Could not find Mediator.Switch.IRequestHandler`2");
        _iPipelineBehaviorSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.IPipelineBehavior`2") ?? throw new InvalidOperationException("Could not find Mediator.Switch.IPipelineBehavior`2");
        // New Interface Lookup
        _iNotificationPipelineBehaviorSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.INotificationPipelineBehavior`1") ?? throw new InvalidOperationException("Could not find Mediator.Switch.INotificationPipelineBehavior`1");

        _iNotificationSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.INotification") ?? throw new InvalidOperationException("Could not find Mediator.Switch.INotification");
        _iNotificationHandlerSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.INotificationHandler`1") ?? throw new InvalidOperationException("Could not find Mediator.Switch.INotificationHandler`1");
        _orderAttributeSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.PipelineBehaviorOrderAttribute") ?? throw new InvalidOperationException("Could not find Mediator.Switch.PipelineBehaviorOrderAttribute");
        _switchMediatorAttributeSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.SwitchMediatorAttribute") ?? throw new InvalidOperationException("Could not find Mediator.Switch.SwitchMediatorAttribute");
    }

    public SemanticAnalysis Analyze(List<TypeDeclarationSyntax> types, CancellationToken cancellationToken)
    {
        var requests = new List<(INamedTypeSymbol Class, ITypeSymbol TResponse)>();
        var handlers = new List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse)>();
        var behaviors = new List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters, INamedTypeSymbol? WrapperType)>();
        var notificationBehaviors = new List<(INamedTypeSymbol Class, ITypeSymbol TNotification, IReadOnlyList<ITypeParameterSymbol> TypeParameters)>();
        var notifications = new List<ITypeSymbol>();
        var notificationHandlers = new List<(INamedTypeSymbol Class, ITypeSymbol TNotification)>();
        INamedTypeSymbol? mediatorClass = null;

        // Analyze types discovered by the syntax receiver
        foreach (var typeSyntax in types)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var model = _compilation.GetSemanticModel(typeSyntax.SyntaxTree);
            if (model.GetDeclaredSymbol(typeSyntax, cancellationToken) is not {Kind: SymbolKind.NamedType} typeSymbol)
            {
                continue;
            }

            // Check if this class has [SwitchMediator]
            if (IsSwitchMediator(typeSymbol))
            {
                if (mediatorClass != null)
                {
                    throw new SourceGenerationException("Multiple classes found with [SwitchMediator] attribute. Only one is allowed per assembly.", typeSymbol.Locations[0]);
                }

                if (!typeSyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                {
                    throw new SourceGenerationException($"Class '{typeSymbol.Name}' marked with [SwitchMediator] must be partial.", typeSymbol.Locations[0]);
                }

                mediatorClass = typeSymbol;
            }

            // Proceed with standard analysis
            AnalyzeTypeSymbol(typeSymbol, cancellationToken, requests, handlers, behaviors, notificationBehaviors, notifications, notificationHandlers);
        }

        // Analyze referenced assemblies
        if (!cancellationToken.IsCancellationRequested)
        {
            foreach (var reference in _compilation.References)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (_compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assemblySymbol) continue;
                if (SymbolEqualityComparer.Default.Equals(assemblySymbol, _compilation.Assembly)) continue;

                CollectAndAnalyzeAssemblyTypes(assemblySymbol, cancellationToken, requests, handlers, behaviors, notificationBehaviors, notifications, notificationHandlers);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var requestBehaviors = ProcessRequestBehaviors(requests, behaviors);
        var processedNotificationBehaviors = ProcessNotificationBehaviors(notifications, notificationHandlers, notificationBehaviors);

        return new SemanticAnalysis(
            MediatorClass: mediatorClass,
            RequestSymbol: _iRequestSymbol,
            NotificationSymbol: _iNotificationSymbol,
            Handlers: handlers,
            RequestBehaviors: requestBehaviors,
            NotificationHandlers: notificationHandlers,
            NotificationBehaviors: processedNotificationBehaviors,
            Notifications: notifications
        );
    }

    private bool IsSwitchMediator(INamedTypeSymbol typeSymbol) =>
        typeSymbol.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, _switchMediatorAttributeSymbol));

    private void AnalyzeTypeSymbol(
        INamedTypeSymbol typeSymbol,
        CancellationToken cancellationToken,
        List<(INamedTypeSymbol Class, ITypeSymbol TResponse)> requests,
        List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse)> handlers,
        List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters, INamedTypeSymbol? WrapperType)> behaviors,
        List<(INamedTypeSymbol Class, ITypeSymbol TNotification, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> notificationBehaviors,
        List<ITypeSymbol> notifications,
        List<(INamedTypeSymbol Class, ITypeSymbol TNotification)> notificationHandlers)
    {
        if (cancellationToken.IsCancellationRequested) return;

        if (typeSymbol.IsAbstract || typeSymbol.IsUnboundGenericType)
        {
            TryAddRequest(typeSymbol, requests);
            TryAddPipelineBehavior(typeSymbol, behaviors);
            TryAddNotificationPipelineBehavior(typeSymbol, notificationBehaviors);
            return;
        }

        if (typeSymbol.TypeArguments.Length == 0)
        {
            TryAddRequest(typeSymbol, requests);
            TryAddRequestHandler(typeSymbol, handlers);
            TryAddNotification(typeSymbol, notifications);
            TryAddNotificationHandler(typeSymbol, notificationHandlers);
        }
        else
        {
            TryAddPipelineBehavior(typeSymbol, behaviors);
            TryAddNotificationPipelineBehavior(typeSymbol, notificationBehaviors);
        }
    }

    private void CollectAndAnalyzeAssemblyTypes(
        IAssemblySymbol assemblySymbol,
        CancellationToken cancellationToken,
        List<(INamedTypeSymbol Class, ITypeSymbol TResponse)> requests,
        List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse)> handlers,
        List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters, INamedTypeSymbol? WrapperType)> behaviors,
        List<(INamedTypeSymbol Class, ITypeSymbol TNotification, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> notificationBehaviors,
        List<ITypeSymbol> notifications,
        List<(INamedTypeSymbol Class, ITypeSymbol TNotification)> notificationHandlers)
    {
        VisitNamespace(assemblySymbol.GlobalNamespace);
        return;

        void VisitNamespace(INamespaceSymbol ns)
        {
            if (cancellationToken.IsCancellationRequested) return;

            foreach (var nestedNs in ns.GetNamespaceMembers())
            {
                VisitNamespace(nestedNs);
            }

            foreach (var type in ns.GetTypeMembers())
            {
                AnalyzeTypeSymbol(type, cancellationToken, requests, handlers, behaviors, notificationBehaviors, notifications, notificationHandlers);
                foreach (var nested in type.GetTypeMembers())
                {
                    AnalyzeTypeSymbol(nested, cancellationToken, requests, handlers, behaviors, notificationBehaviors, notifications, notificationHandlers);
                }
            }
        }
    }

    private static bool OriginalDefinitionMatches(ITypeSymbol iface, INamedTypeSymbol target)
    {
        if (SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, target))
            return true;

        if (iface.OriginalDefinition is not INamedTypeSymbol ifaceDef) return false;

        if (ifaceDef.MetadataName == target.MetadataName &&
            ifaceDef.ContainingNamespace?.ToDisplayString() == target.ContainingNamespace?.ToDisplayString())
            return true;

        return false;
    }

    private void TryAddRequest(INamedTypeSymbol typeSymbol, List<(INamedTypeSymbol Class, ITypeSymbol TResponse)> requests)
    {
        var requestInterface = typeSymbol.AllInterfaces.FirstOrDefault(i =>
            OriginalDefinitionMatches(i, _iRequestSymbol));

        if (requestInterface != null)
        {
            var tResponse = requestInterface.TypeArguments[0];
            if (!requests.Any(r => SymbolEqualityComparer.Default.Equals(r.Class, typeSymbol)))
            {
                requests.Add((typeSymbol, tResponse));
            }
        }
    }

    private void TryAddRequestHandler(INamedTypeSymbol typeSymbol, List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse)> handlers)
    {
        var handlerInterfaces = typeSymbol.AllInterfaces.Where(i =>
            OriginalDefinitionMatches(i, _iRequestHandlerSymbol));

        foreach (var handlerInterface in handlerInterfaces)
        {
            if (handlerInterface.TypeArguments.Length != 2) continue;

            var tRequest = handlerInterface.TypeArguments[0];
            var tResponse = handlerInterface.TypeArguments[1];

            if (tRequest.Kind == SymbolKind.TypeParameter || tResponse.Kind == SymbolKind.TypeParameter) continue;

            // --- DELETED: VerifyRequestMatchesHandler(typeSymbol, tRequest); ---
            // The logic is now in Mediator.Switch.Analyzer

            if (!handlers.Any(h => SymbolEqualityComparer.Default.Equals(h.Class, typeSymbol) && SymbolEqualityComparer.Default.Equals(h.TRequest, tRequest)))
            {
                handlers.Add((typeSymbol, tRequest, tResponse));
            }
        }
    }

    private void TryAddPipelineBehavior(INamedTypeSymbol typeSymbol, List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters, INamedTypeSymbol? WrapperType)> behaviors)
    {
        var behaviorInterface = typeSymbol.AllInterfaces.FirstOrDefault(i =>
            OriginalDefinitionMatches(i, _iPipelineBehaviorSymbol));

        if (behaviorInterface == null) return;

        var classTypeParameters = typeSymbol.TypeParameters;
        var location = typeSymbol.Locations.FirstOrDefault() ?? Location.None;

        if (classTypeParameters.Length != 2)
        {
            throw new SourceGenerationException(
                $"Pipeline behavior class '{typeSymbol.ToDisplayString()}' must have exactly two generic type parameters.", location);
        }

        var classTRequest = classTypeParameters[0];
        var classTResponse = classTypeParameters[1];
        var interfaceTRequest = behaviorInterface.TypeArguments[0];
        var interfaceTResponse = behaviorInterface.TypeArguments[1];

        if (!SymbolEqualityComparer.Default.Equals(interfaceTRequest, classTRequest))
        {
            throw new SourceGenerationException(
                $"The first type argument to IPipelineBehavior in '{typeSymbol.ToDisplayString()}' must be the behavior's first generic type parameter.", location);
        }

        INamedTypeSymbol? wrapperType = null;
        if (!SymbolEqualityComparer.Default.Equals(interfaceTResponse, classTResponse))
        {
            if (interfaceTResponse is INamedTypeSymbol {IsGenericType: true, TypeArguments.Length: 1} namedInterfaceTResponse &&
                SymbolEqualityComparer.Default.Equals(namedInterfaceTResponse.TypeArguments[0], classTResponse))
            {
                var potentialWrapperDef = namedInterfaceTResponse.OriginalDefinition;
                if (potentialWrapperDef is {IsGenericType: true, TypeParameters.Length: 1})
                {
                    wrapperType = potentialWrapperDef;
                }
                else
                {
                    throw new SourceGenerationException($"Invalid wrapper type in behavior '{typeSymbol.ToDisplayString()}'.", location);
                }
            }
            else
            {
                throw new SourceGenerationException($"Invalid TResponse in behavior '{typeSymbol.ToDisplayString()}'.", location);
            }
        }

        var typeParameters = typeSymbol.TypeParameters;
        if (!behaviors.Any(b => SymbolEqualityComparer.Default.Equals(b.Class, typeSymbol) && SymbolEqualityComparer.Default.Equals(b.TRequest, interfaceTRequest) && SymbolEqualityComparer.Default.Equals(b.TResponse, interfaceTResponse)))
        {
            behaviors.Add((typeSymbol, interfaceTRequest, interfaceTResponse, typeParameters, wrapperType));
        }
    }

    private void TryAddNotificationPipelineBehavior(INamedTypeSymbol typeSymbol, List<(INamedTypeSymbol Class, ITypeSymbol TNotification, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> behaviors)
    {
        var behaviorInterface = typeSymbol.AllInterfaces.FirstOrDefault(i =>
            OriginalDefinitionMatches(i, _iNotificationPipelineBehaviorSymbol));

        if (behaviorInterface == null) return;

        var classTypeParameters = typeSymbol.TypeParameters;
        var location = typeSymbol.Locations.FirstOrDefault() ?? Location.None;

        if (classTypeParameters.Length != 1)
        {
            throw new SourceGenerationException(
                $"Notification pipeline behavior class '{typeSymbol.ToDisplayString()}' must have exactly one generic type parameter (e.g., MyBehavior<TNotification>).",
                location);
        }

        var classTNotification = classTypeParameters[0];
        var interfaceTNotification = behaviorInterface.TypeArguments[0];

        if (!SymbolEqualityComparer.Default.Equals(interfaceTNotification, classTNotification))
        {
            throw new SourceGenerationException(
                $"The type argument to INotificationPipelineBehavior in '{typeSymbol.ToDisplayString()}' must be the behavior's generic type parameter.",
                location);
        }

        if (!behaviors.Any(b => SymbolEqualityComparer.Default.Equals(b.Class, typeSymbol) && SymbolEqualityComparer.Default.Equals(b.TNotification, interfaceTNotification)))
        {
            behaviors.Add((typeSymbol, interfaceTNotification, typeSymbol.TypeParameters));
        }
    }

    private void TryAddNotification(INamedTypeSymbol typeSymbol, List<ITypeSymbol> foundNotificationTypes)
    {
        var notificationInterface = typeSymbol.AllInterfaces.FirstOrDefault(i =>
            OriginalDefinitionMatches(i, _iNotificationSymbol));

        if (notificationInterface != null)
        {
            if (!foundNotificationTypes.Contains(typeSymbol, SymbolEqualityComparer.Default))
            {
                foundNotificationTypes.Add(typeSymbol);
            }
        }
    }

    private void TryAddNotificationHandler(INamedTypeSymbol typeSymbol, List<(INamedTypeSymbol Class, ITypeSymbol TNotification)> notificationHandlers)
    {
        var notificationHandlerInterfaces = typeSymbol.AllInterfaces.Where(i =>
            OriginalDefinitionMatches(i, _iNotificationHandlerSymbol));

        foreach (var notificationHandlerInterface in notificationHandlerInterfaces)
        {
            if (notificationHandlerInterface == null) continue;

            var notification = notificationHandlerInterface.TypeArguments.FirstOrDefault();
            if (notification != null)
            {
                if (!notificationHandlers.Any(h => SymbolEqualityComparer.Default.Equals(h.Class, typeSymbol) && SymbolEqualityComparer.Default.Equals(h.TNotification, notification)))
                {
                    notificationHandlers.Add((typeSymbol, notification));
                }
            }
        }
    }

    private List<((INamedTypeSymbol Class, ITypeSymbol TResponse) Request, List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> Behaviors)>
        ProcessRequestBehaviors(
            List<(INamedTypeSymbol Class, ITypeSymbol TResponse)> requests,
            List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters, INamedTypeSymbol? WrapperType)> behaviors) =>
        requests.Select(request =>
        {
            var applicableBehaviors = GetApplicableBehaviors(behaviors, request);
            return (Request: request, Behaviors: applicableBehaviors.OrderByDescending(b => GetOrder(b.Class)).ToList());
        }).ToList();

    private IEnumerable<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> GetApplicableBehaviors(List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters, INamedTypeSymbol? WrapperType)> behaviors, (INamedTypeSymbol Class, ITypeSymbol TResponse) request)
    {
        foreach (var behavior in behaviors)
        {
            ITypeSymbol actualBehaviorTResponse;

            if (behavior.WrapperType == null)
            {
                actualBehaviorTResponse = request.TResponse;
            }
            else
            {
                if (request.TResponse is INamedTypeSymbol {IsGenericType: true} requestResponseNamedType &&
                    SymbolEqualityComparer.Default.Equals(requestResponseNamedType.OriginalDefinition, behavior.WrapperType) &&
                    requestResponseNamedType.TypeArguments.Length == 1)
                {
                    actualBehaviorTResponse = requestResponseNamedType.TypeArguments[0];
                }
                else
                {
                    continue;
                }
            }

            var constraintsMet = BehaviorApplicabilityChecker.IsApplicable(
                _compilation,
                behavior.Class,
                behavior.TypeParameters,
                request.Class,
                actualBehaviorTResponse
            );

            if (constraintsMet)
            {
                yield return (
                    behavior.Class,
                    request.Class,
                    actualBehaviorTResponse,
                    behavior.TypeParameters
                );
            }
        }
    }

    private List<((ITypeSymbol Notification, ITypeSymbol ActualNotification) NotificationInfo, List<(INamedTypeSymbol Class, ITypeSymbol TNotification, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> Behaviors)>
        ProcessNotificationBehaviors(
            List<ITypeSymbol> notifications,
            List<(INamedTypeSymbol Class, ITypeSymbol TNotification)> notificationHandlers,
            List<(INamedTypeSymbol Class, ITypeSymbol TNotification, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> behaviors)
    {
        var results = new List<((ITypeSymbol Notification, ITypeSymbol ActualNotification), List<(INamedTypeSymbol Class, ITypeSymbol TNotification, IReadOnlyList<ITypeParameterSymbol> TypeParameters)>)>();

        // We need to determine the "Actual" notification type for every discovered notification,
        // similar to how CodeGenerator does it, so we can match behaviors to the concrete type being switched on.
        // However, CodeGenerator logic TryGetActualNotification is static. We duplicate logic slightly or rely on the fact
        // that we iterate all notifications.

        foreach (var notification in notifications)
        {
            var actualNotification = TryGetActualNotification(_iNotificationSymbol, notificationHandlers, notification);
            if (actualNotification == null) continue;

            var applicable = new List<(INamedTypeSymbol Class, ITypeSymbol TNotification, IReadOnlyList<ITypeParameterSymbol> TypeParameters)>();

            foreach (var behavior in behaviors)
            {
                if (BehaviorApplicabilityChecker.IsApplicable(_compilation, behavior.Class, behavior.TypeParameters, actualNotification))
                {
                    applicable.Add(behavior);
                }
            }

            results.Add(((notification, actualNotification), applicable.OrderByDescending(b => GetOrder(b.Class)).ToList()));
        }

        return results;
    }

    // Duplicated helper from CodeGenerator to ensure consistency in analysis phase
    private static ITypeSymbol? TryGetActualNotification(
        ITypeSymbol iNotificationType,
        List<(INamedTypeSymbol Class, ITypeSymbol TNotification)> notificationHandlers,
        ITypeSymbol notification)
    {
        var current = notification;
        do
        {
            var handler = notificationHandlers.FirstOrDefault(h =>
                h.TNotification.Equals(current, SymbolEqualityComparer.Default));
            if (handler != default)
            {
                return current;
            }
            current = current.BaseType;
        } while (current != null &&
                 current.AllInterfaces.Any(i =>
                     SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, iNotificationType)));

        return null;
    }

    private int GetOrder(ITypeSymbol typeSymbol)
    {
        var orderAttribute = typeSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass != null && OriginalDefinitionMatches(attr.AttributeClass, _orderAttributeSymbol));

        return orderAttribute?.ConstructorArguments.Length > 0 && orderAttribute.ConstructorArguments[0].Value is int order
               ? order
               : int.MaxValue;
    }
}