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
    private readonly INamedTypeSymbol _iNotificationSymbol;

    public SemanticAnalyzer(Compilation compilation)
    {
        _compilation = compilation;
            
        var iRequestSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.IRequest`1"); 
        var iRequestHandlerSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.IRequestHandler`2");
        var iPipelineBehaviorSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.IPipelineBehavior`2");
        var iNotificationSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.INotification");
            
        var iNotificationHandlerSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.INotificationHandler`1");
        var orderAttributeSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.PipelineBehaviorOrderAttribute");

        if (iRequestSymbol == null || iRequestHandlerSymbol == null || iPipelineBehaviorSymbol == null ||
            iNotificationSymbol == null || iNotificationHandlerSymbol == null || orderAttributeSymbol == null)
        {
            throw new InvalidOperationException("Could not find required Mediator.Switch types.");
        }

        _iRequestSymbol = iRequestSymbol;
        _iRequestHandlerSymbol = iRequestHandlerSymbol;
        _iPipelineBehaviorSymbol = iPipelineBehaviorSymbol;
        _iNotificationSymbol = iNotificationSymbol;
    }

    public (List<(ITypeSymbol Class, ITypeSymbol TResponse)> Requests,
        List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse)> Handlers,
        List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> Behaviors,
        List<ITypeSymbol> Notifications)
        Analyze(List<ClassDeclarationSyntax> classes)
    {
        var requests = new List<(ITypeSymbol Class, ITypeSymbol TResponse)>();
        var handlers = new List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse)>();
        var behaviors = new List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)>();
        var notifications = new List<ITypeSymbol>();

        foreach (var classSyntax in classes)
        {
            var model = _compilation.GetSemanticModel(classSyntax.SyntaxTree);
            var classSymbol = model.GetDeclaredSymbol(classSyntax);
            if (classSymbol == null) continue;

            var requestInterface = classSymbol.AllInterfaces.FirstOrDefault(i =>
                i.OriginalDefinition.Equals(_iRequestSymbol, SymbolEqualityComparer.Default));
            if (requestInterface != null)
            {
                var tResponse = requestInterface.TypeArguments[0];
                requests.Add((classSymbol, tResponse));
            }

            var handlerInterface = classSymbol.AllInterfaces.FirstOrDefault(i =>
                i.OriginalDefinition.Equals(_iRequestHandlerSymbol, SymbolEqualityComparer.Default));
            if (handlerInterface != null)
            {
                var tRequest = handlerInterface.TypeArguments[0];
                var tResponse = handlerInterface.TypeArguments[1];
                handlers.Add((classSymbol, tRequest, tResponse));
            }

            var behaviorInterface = classSymbol.AllInterfaces.FirstOrDefault(i =>
                i.OriginalDefinition.Equals(_iPipelineBehaviorSymbol, SymbolEqualityComparer.Default));
            if (behaviorInterface != null)
            {
                var tRequest = behaviorInterface.TypeArguments[0];
                var tResponse = behaviorInterface.TypeArguments[1];
                var typeParameters = classSymbol.TypeParameters; // Capture constraints
                behaviors.Add((classSymbol, tRequest, tResponse, typeParameters));
            }

            if (classSymbol.AllInterfaces.Any(i => i.Equals(_iNotificationSymbol, SymbolEqualityComparer.Default)))
            {
                notifications.Add(classSymbol);
            }
        }

        return (requests, handlers, behaviors, notifications);
    }
}