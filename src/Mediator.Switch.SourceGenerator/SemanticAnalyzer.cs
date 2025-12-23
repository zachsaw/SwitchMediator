using Mediator.Switch.SourceGenerator.Exceptions;
using Mediator.Switch.SourceGenerator.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mediator.Switch.SourceGenerator;

public class SemanticAnalyzer
{
    private const string AdaptorAttributeName = "PipelineBehaviorResponseAdaptorAttribute";
    private const string AdaptorAttributeGenericsTypeName = $"{AdaptorAttributeName}.GenericsType";

    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol _iRequestSymbol;
    private readonly INamedTypeSymbol _iRequestHandlerSymbol;
    private readonly INamedTypeSymbol _iPipelineBehaviorSymbol;
    private readonly INamedTypeSymbol _iNotificationSymbol;
    private readonly INamedTypeSymbol _iNotificationHandlerSymbol;
    private readonly INamedTypeSymbol _responseAdaptorAttributeSymbol;
    private readonly INamedTypeSymbol _orderAttributeSymbol;
    private readonly INamedTypeSymbol _requestHandlerAttributeSymbol;

    public SemanticAnalyzer(Compilation compilation)
    {
        _compilation = compilation;

        _ = compilation.GetTypeByMetadataName("Mediator.Switch.IMediator") ?? throw new InvalidOperationException("Could not find Mediator.Switch.IMediator");
        _ = compilation.GetTypeByMetadataName("Mediator.Switch.ISender") ?? throw new InvalidOperationException("Could not find Mediator.Switch.ISender");
        _ = compilation.GetTypeByMetadataName("Mediator.Switch.IPublisher") ?? throw new InvalidOperationException("Could not find Mediator.Switch.IPublisher");

        _iRequestSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.IRequest`1") ?? throw new InvalidOperationException("Could not find Mediator.Switch.IRequest`1");
        _iRequestHandlerSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.IRequestHandler`2") ?? throw new InvalidOperationException("Could not find Mediator.Switch.IRequestHandler`2");
        _iPipelineBehaviorSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.IPipelineBehavior`2") ?? throw new InvalidOperationException("Could not find Mediator.Switch.IPipelineBehavior`2");
        _iNotificationSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.INotification") ?? throw new InvalidOperationException("Could not find Mediator.Switch.INotification");
        _iNotificationHandlerSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.INotificationHandler`1") ?? throw new InvalidOperationException("Could not find Mediator.Switch.INotificationHandler`1");
        _orderAttributeSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.PipelineBehaviorOrderAttribute") ?? throw new InvalidOperationException("Could not find Mediator.Switch.PipelineBehaviorOrderAttribute");
        _responseAdaptorAttributeSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.PipelineBehaviorResponseAdaptorAttribute") ?? throw new InvalidOperationException("Could not find Mediator.Switch.PipelineBehaviorResponseAdaptorAttribute");
        _requestHandlerAttributeSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.RequestHandlerAttribute") ?? throw new InvalidOperationException("Could not find Mediator.Switch.RequestHandlerAttribute");
    }

    public SemanticAnalysis Analyze(List<TypeDeclarationSyntax> types, CancellationToken cancellationToken)
    {
        var requests = new List<(INamedTypeSymbol Class, ITypeSymbol TResponse)>();
        var handlers = new List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse)>();
        var behaviors = new List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters, INamedTypeSymbol? WrapperType)>();
        var notifications = new List<ITypeSymbol>();
        var notificationHandlers = new List<(INamedTypeSymbol Class, ITypeSymbol TNotification)>();

        // Analyze types discovered by the syntax receiver (these are source symbols from the current project)
        foreach (var typeSyntax in types)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            AnalyzeTypeSyntax(typeSyntax, cancellationToken, requests, handlers, behaviors, notifications, notificationHandlers);
        }

        // Also analyze referenced assemblies so that handlers/requests defined in referenced projects (metadata references)
        // are discovered and included in generation.
        if (!cancellationToken.IsCancellationRequested)
        {
            foreach (var reference in _compilation.References)
            {
                if (cancellationToken.IsCancellationRequested) break;

                if (_compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assemblySymbol) continue;

                // Skip the current compilation's assembly (we already analyzed source types)
                if (SymbolEqualityComparer.Default.Equals(assemblySymbol, _compilation.Assembly))
                    continue;

                CollectAndAnalyzeAssemblyTypes(assemblySymbol, cancellationToken, requests, handlers, behaviors, notifications, notificationHandlers);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var requestBehaviors = ProcessRequestBehaviors(requests, behaviors);

        // Return a SemanticAnalysis record bundling the discovered symbols and results
        return new SemanticAnalysis(
            RequestSymbol: _iRequestSymbol,
            NotificationSymbol: _iNotificationSymbol,
            Handlers: handlers,
            RequestBehaviors: requestBehaviors,
            NotificationHandlers: notificationHandlers,
            Notifications: notifications
        );
    }

    private void AnalyzeTypeSyntax(
        TypeDeclarationSyntax typeSyntax,
        CancellationToken cancellationToken,
        List<(INamedTypeSymbol Class, ITypeSymbol TResponse)> requests,
        List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse)> handlers,
        List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters, INamedTypeSymbol? WrapperType)> behaviors,
        List<ITypeSymbol> notifications,
        List<(INamedTypeSymbol Class, ITypeSymbol TNotification)> notificationHandlers)
    {
        var model = _compilation.GetSemanticModel(typeSyntax.SyntaxTree);
        if (model.GetDeclaredSymbol(typeSyntax, cancellationToken) is not {Kind: SymbolKind.NamedType} typeSymbol)
        {
            return;
        }

        AnalyzeTypeSymbol(typeSymbol, cancellationToken, requests, handlers, behaviors, notifications, notificationHandlers);
    }

    private void AnalyzeTypeSymbol(
        INamedTypeSymbol typeSymbol,
        CancellationToken cancellationToken,
        List<(INamedTypeSymbol Class, ITypeSymbol TResponse)> requests,
        List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse)> handlers,
        List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters, INamedTypeSymbol? WrapperType)> behaviors,
        List<ITypeSymbol> notifications,
        List<(INamedTypeSymbol Class, ITypeSymbol TNotification)> notificationHandlers)
    {
        if (cancellationToken.IsCancellationRequested) return;

        if (typeSymbol.IsAbstract || typeSymbol.IsUnboundGenericType)
        {
            TryAddRequest(typeSymbol, requests);
            TryAddPipelineBehavior(typeSymbol, behaviors);
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
        }
    }

    private void CollectAndAnalyzeAssemblyTypes(
        IAssemblySymbol assemblySymbol,
        CancellationToken cancellationToken,
        List<(INamedTypeSymbol Class, ITypeSymbol TResponse)> requests,
        List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse)> handlers,
        List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters, INamedTypeSymbol? WrapperType)> behaviors,
        List<ITypeSymbol> notifications,
        List<(INamedTypeSymbol Class, ITypeSymbol TNotification)> notificationHandlers)
    {
        // Walk the assembly's namespace/type tree and analyze every named type we encounter.
        void VisitNamespace(INamespaceSymbol ns)
        {
            if (cancellationToken.IsCancellationRequested) return;

            foreach (var nestedNs in ns.GetNamespaceMembers())
            {
                VisitNamespace(nestedNs);
            }

            foreach (var type in ns.GetTypeMembers())
            {
                // We only care about named types (classes/records/structs). Analyze them directly.
                AnalyzeTypeSymbol(type, cancellationToken, requests, handlers, behaviors, notifications, notificationHandlers);

                // Also analyze nested types
                foreach (var nested in type.GetTypeMembers())
                {
                    AnalyzeTypeSymbol(nested, cancellationToken, requests, handlers, behaviors, notifications, notificationHandlers);
                }
            }
        }

        VisitNamespace(assemblySymbol.GlobalNamespace);
    }

    private static bool OriginalDefinitionMatches(ITypeSymbol iface, INamedTypeSymbol target)
    {
        if (SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, target))
            return true;

        if (iface.OriginalDefinition is not INamedTypeSymbol ifaceDef) return false;

        // Compare by metadata name and namespace as a fallback for metadata references
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
            // Avoid duplicates
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
            if (handlerInterface.TypeArguments.Length != 2)
                continue;

            var tRequest = handlerInterface.TypeArguments[0];
            var tResponse = handlerInterface.TypeArguments[1];

            if (tRequest.Kind == SymbolKind.TypeParameter || tResponse.Kind == SymbolKind.TypeParameter)
                continue;

            VerifyRequestMatchesHandler(typeSymbol, tRequest);

            // Avoid duplicates
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

        if (behaviorInterface == null)
        {
            return;
        }

        var classTypeParameters = typeSymbol.TypeParameters;
        var location = typeSymbol.Locations.FirstOrDefault() ?? Location.None;

        if (classTypeParameters.Length != 2)
        {
            throw new SourceGenerationException(
                $"Pipeline behavior class '{typeSymbol.ToDisplayString()}' must have exactly two generic type parameters (e.g., MyBehavior<TRequest, TResponse>).",
                location);
        }

        var classTRequest = classTypeParameters[0]; // e.g., TRequest from MyBehavior<TRequest, TResponse>
        var classTResponse = classTypeParameters[1]; // e.g., TResponse from MyBehavior<TRequest, TResponse> (the "inner" one)

        var interfaceTRequest = behaviorInterface.TypeArguments[0]; // T1 from IPipelineBehavior<T1, T2>
        var interfaceTResponse = behaviorInterface.TypeArguments[1]; // T2 from IPipelineBehavior<T1, T2>

        if (!SymbolEqualityComparer.Default.Equals(interfaceTRequest, classTRequest))
        {
            throw new SourceGenerationException(
                $"The first type argument to IPipelineBehavior in '{typeSymbol.ToDisplayString()}' is '{interfaceTRequest.ToDisplayString()}', but it must be the behavior's first generic type parameter '{classTRequest.ToDisplayString()}'. " +
                "Directly using closed or constructed generic types for TRequest in IPipelineBehavior is not supported.",
                location);
        }

        // Check TResponse part of IPipelineBehavior
        INamedTypeSymbol? wrapperType = null;
        if (SymbolEqualityComparer.Default.Equals(interfaceTResponse, classTResponse))
        {
            // Case 1: Direct match. IPipelineBehavior<TClassRequest, TClassResponse>
            // No wrapper. inferredWrapperTypeOriginalDefinition remains null.
        }
        else if (interfaceTResponse is INamedTypeSymbol {IsGenericType: true, TypeArguments.Length: 1} namedInterfaceTResponse &&
                 SymbolEqualityComparer.Default.Equals(namedInterfaceTResponse.TypeArguments[0], classTResponse))
        {
            // Case 2: Potential wrapped response. IPipelineBehavior<TClassRequest, SomeWrapper<TClassResponse>>
            var potentialWrapperDef = namedInterfaceTResponse.OriginalDefinition;
            if (potentialWrapperDef is {IsGenericType: true, TypeParameters.Length: 1})
            {
                wrapperType = potentialWrapperDef;
            }
            else
            {
                // The structure is SomeWrapper<TClassResponse>, but SomeWrapper<> itself is not a valid unbound generic.
                throw new SourceGenerationException(
                    $"The response type '{interfaceTResponse.ToDisplayString()}' in IPipelineBehavior for '{typeSymbol.ToDisplayString()}' appears to use a wrapper around the behavior's second generic parameter '{classTResponse.ToDisplayString()}', " +
                    $"but the wrapper '{potentialWrapperDef.ToDisplayString()}' is not a valid unbound generic type with one type parameter (e.g., 'Result<>').",
                    location);
            }
        }
        else
        {
            // Case 3: Invalid TResponse structure.
            // It's not TClassResponse, and it's not a valid SomeWrapper<TClassResponse>.
            // This catches IPipelineBehavior<TRequest, Foo<int>>, IPipelineBehavior<TRequest, SomeWrapper<string>> when classTResponse is TResp, etc.
            throw new SourceGenerationException(
                $"The second type argument to IPipelineBehavior in '{typeSymbol.ToDisplayString()}' (which is '{interfaceTResponse.ToDisplayString()}') " +
                $"must either be the behavior's second generic type parameter ('{classTResponse.ToDisplayString()}') " +
                $"or a generic wrapper around it where the wrapper is an unbound generic type with one argument (e.g., 'Result<{classTResponse.ToDisplayString()}>').",
                location);
        }

        var typeParameters = typeSymbol.TypeParameters;
        VerifyAdaptorMatchesTResponse(typeSymbol, interfaceTResponse); // Verify attribute if present (obsolete)
        // Avoid duplicates
        if (!behaviors.Any(b => SymbolEqualityComparer.Default.Equals(b.Class, typeSymbol) && SymbolEqualityComparer.Default.Equals(b.TRequest, interfaceTRequest) && SymbolEqualityComparer.Default.Equals(b.TResponse, interfaceTResponse)))
        {
            behaviors.Add((typeSymbol, interfaceTRequest, interfaceTResponse, typeParameters, wrapperType));
        }
    }

    private void TryAddNotification(INamedTypeSymbol typeSymbol, List<ITypeSymbol> foundNotificationTypes)
    {
        var notificationInterface = typeSymbol.AllInterfaces.FirstOrDefault(i =>
            OriginalDefinitionMatches(i, _iNotificationSymbol));

        if (notificationInterface != null)
        {
            // Avoid adding duplicates if a type appears multiple times (e.g., partial classes)
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
            if (notificationHandlerInterface == null)
                continue;

            var notification = notificationHandlerInterface.TypeArguments.FirstOrDefault();
            if (notification != null)
            {
                // Avoid duplicates
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

    private void VerifyRequestMatchesHandler(INamedTypeSymbol classSymbol, ITypeSymbol handledType)
    {
        var requestHandlerAttribute = handledType.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass != null && OriginalDefinitionMatches(attr.AttributeClass, _requestHandlerAttributeSymbol));

        if (requestHandlerAttribute == null || requestHandlerAttribute.ConstructorArguments.Length == 0) return;

        if (requestHandlerAttribute.ConstructorArguments.Single().Value is INamedTypeSymbol attributeSymbol)
        {
            if (!SymbolEqualityComparer.Default.Equals(attributeSymbol, classSymbol))
            {
                var syntaxReference = requestHandlerAttribute.ApplicationSyntaxReference;
                throw new SourceGenerationException(
                    $"RequestHandlerAttribute: Handler type mismatch on '{handledType.ToDisplayString()}' - expecting handler '{classSymbol.ToDisplayString()}' but attribute points to '{attributeSymbol.ToDisplayString()}'.",
                    syntaxReference != null ? Location.Create(syntaxReference.SyntaxTree, syntaxReference.Span) : Location.None);
            }
        }
    }

    private void VerifyAdaptorMatchesTResponse(INamedTypeSymbol classSymbol, ITypeSymbol tResponse)
    {
        var responseTypeAdaptorAttribute = classSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass != null && OriginalDefinitionMatches(attr.AttributeClass, _responseAdaptorAttributeSymbol));

        if (responseTypeAdaptorAttribute == null) return;

        var responseWrapperType = GetAdaptorWrapperType(responseTypeAdaptorAttribute);
        if (tResponse is not INamedTypeSymbol unwrappedResponseType ||
            !SymbolEqualityComparer.Default.Equals(unwrappedResponseType.OriginalDefinition, responseWrapperType.OriginalDefinition))
        {
            var syntaxReference = responseTypeAdaptorAttribute.ApplicationSyntaxReference;
            throw new SourceGenerationException($"{AdaptorAttributeGenericsTypeName} does not match IPipelineBehavior's TResponse argument.",
                syntaxReference != null ? Location.Create(syntaxReference.SyntaxTree, syntaxReference.Span) : Location.None);
        }
    }

    private static INamedTypeSymbol GetAdaptorWrapperType(AttributeData responseTypeAdaptorAttribute)
    {
        if (responseTypeAdaptorAttribute.ConstructorArguments.Length != 1 ||
            responseTypeAdaptorAttribute.ConstructorArguments[0].Value is not INamedTypeSymbol typeArgSymbol)
        {
            var syntaxReference = responseTypeAdaptorAttribute.ApplicationSyntaxReference;
            throw new SourceGenerationException($"{AdaptorAttributeName} requires a single 'typeof(UnboundGeneric<>)' argument.",
                syntaxReference != null ? Location.Create(syntaxReference.SyntaxTree, syntaxReference.Span) : Location.None);
        }

        if (!typeArgSymbol.IsUnboundGenericType || typeArgSymbol.TypeParameters.Length != 1)
        {
            var syntaxReference = responseTypeAdaptorAttribute.ApplicationSyntaxReference;
            throw new SourceGenerationException($"{AdaptorAttributeGenericsTypeName} must be an unbound generic type with 1 argument (e.g., typeof(Wrapper<>)).",
                syntaxReference != null ? Location.Create(syntaxReference.SyntaxTree, syntaxReference.Span) : Location.None);
        }

        return typeArgSymbol;
    }
    
    private int GetOrder(ITypeSymbol typeSymbol)
    {
        var orderAttribute = typeSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass != null && OriginalDefinitionMatches(attr.AttributeClass, _orderAttributeSymbol));

        return orderAttribute?.ConstructorArguments.Length > 0 && orderAttribute.ConstructorArguments[0].Value is int order
               ? order
               : int.MaxValue; // Default order if attribute is missing or invalid
    }
}