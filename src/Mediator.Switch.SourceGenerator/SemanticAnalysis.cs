using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator;

/// <summary>
/// Aggregated semantic analysis results produced by <see cref="SemanticAnalyzer"/>.
/// </summary>
public record SemanticAnalysis(
    INamedTypeSymbol? MediatorClass,
    INamedTypeSymbol RequestSymbol,
    INamedTypeSymbol NotificationSymbol,
    List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse)> Handlers,
    List<((INamedTypeSymbol Class, ITypeSymbol TResponse) Request, List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> Behaviors)> RequestBehaviors,
    List<(INamedTypeSymbol Class, ITypeSymbol TNotification)> NotificationHandlers,
    List<((ITypeSymbol Notification, ITypeSymbol ActualNotification) NotificationInfo, List<(INamedTypeSymbol Class, ITypeSymbol TNotification, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> Behaviors)> NotificationBehaviors,
    List<ITypeSymbol> Notifications
);