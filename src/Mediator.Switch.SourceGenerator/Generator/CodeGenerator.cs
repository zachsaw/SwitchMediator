using Mediator.Switch.SourceGenerator.Extensions;
using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Generator;

public static class CodeGenerator
{
    // Define a custom format based on FullyQualifiedFormat but WITH nullable annotations
    private static readonly SymbolDisplayFormat _fqn =
        SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(
                SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
            );

    public static string Generate(SemanticAnalysis analysis)
    {
        var iRequestType = analysis.RequestSymbol;
        var iNotificationType = analysis.NotificationSymbol;
        var handlers = analysis.Handlers;
        var requestBehaviors = analysis.RequestBehaviors;
        var notificationHandlers = analysis.NotificationHandlers;
        var notifications = analysis.Notifications;
        var notificationBehaviors = analysis.NotificationBehaviors;

        var actualSendCases = requestBehaviors
            .OrderBy(r => r.Request.Class, new TypeHierarchyComparer(iRequestType, requestBehaviors.Select(r => r.Request.Class)))
            .Select(r => (r.Request, ActualRequest: TryGetActualRequest(iRequestType, handlers, r.Request)!))
            .Where(c => c.ActualRequest != null)
            .ToList();

        var usedRequests = new HashSet<ITypeSymbol>(actualSendCases.Select(c => c.ActualRequest), SymbolEqualityComparer.Default);
        var actualRequestBehaviors = requestBehaviors.Where(r => usedRequests.Contains(r.Request.Class)).ToList();

        var actualPublishCases = notifications
            .OrderBy(n => n, new TypeHierarchyComparer(iRequestType, notifications.Select(n => n)))
            .Select(n => (Notification: n, ActualNotification: TryGetActualNotification(iNotificationType, notificationHandlers, n)!))
            .Where(c => c.ActualNotification != null)
            .ToList();

        var usedNotifications = new HashSet<ITypeSymbol>(actualPublishCases.Select(c => c.ActualNotification), SymbolEqualityComparer.Default);

        // Generate fields
        var handlerFields = handlers
            .Select(h => h.Class)
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
            .Select(cls => $"private {cls.ToDisplayString(_fqn)}? _{cls.GetVariableName()};");

        // Generate behavior fields specific to each request
        var behaviorFields = actualRequestBehaviors.SelectMany(r =>
        {
            var (request, applicableBehaviors) = r;
            return applicableBehaviors.Select(b =>
                $"private {b.Class.ToDisplayString(_fqn).DropGenerics()}<{request.Class.ToDisplayString(_fqn)}, {b.TResponse.ToDisplayString(_fqn)}>? _{b.Class.GetVariableName()}__{request.Class.GetVariableName()};");
        });

        // Generate behavior fields specific to each notification
        var notificationBehaviorFields = notificationBehaviors
            .Where(nb => usedNotifications.Contains(nb.NotificationInfo.ActualNotification))
            .SelectMany(nb =>
            {
                var actualNotif = nb.NotificationInfo.ActualNotification;
                return nb.Behaviors.Select(b =>
                    $"private {b.Class.ToDisplayString(_fqn).DropGenerics()}<{actualNotif.ToDisplayString(_fqn)}>? _{b.Class.GetVariableName()}__{actualNotif.GetVariableName()};");
            });

        // Use global:: for system types to avoid collisions
        var notificationHandlerFields = usedNotifications.Select(n =>
            $"private global::System.Collections.Generic.IEnumerable<global::Mediator.Switch.INotificationHandler<{n.ToDisplayString(_fqn)}>>? _{n.GetVariableName()}__Handlers;");

        // Generate Send method switch cases
        var sendCases = actualSendCases
            .Select(c => GenerateSendCase(c.ActualRequest, c.Request));

        // Generate behavior chain methods for requests
        var behaviorMethods = actualRequestBehaviors
            .Select(r => TryGenerateBehaviorMethod(handlers, r))
            .Where(m => m != null);

        // Generate Publish method switch cases
        var publishCases = actualPublishCases
            .Select(n => GeneratePublishCase(n.ActualNotification, n.Notification, notificationBehaviors));

        // Generate known types
        var requestHandlerTypes = handlers
            .Select(h => h.Class)
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
            .Select(cls => $"typeof({cls.ToDisplayString(_fqn)})");

        var notificationTypes = usedNotifications.Select(n =>
            $"(typeof({n.ToDisplayString(_fqn)}), new global::System.Type[] {{\n                    {string.Join(",\n                    ", notificationHandlers
                .Where(h => h.TNotification.Equals(n, SymbolEqualityComparer.Default))
                .Select(h => $"typeof({h.Class.ToDisplayString(_fqn)})"))}\n                }})");

        var pipelineBehaviorTypes = actualRequestBehaviors.SelectMany(r =>
        {
            var (request, applicableBehaviors) = r;
            return applicableBehaviors.Select(b =>
                $"typeof({b.Class.ToDisplayString(_fqn).DropGenerics()}<{request.Class.ToDisplayString(_fqn)}, {b.TResponse.ToDisplayString(_fqn)}>)");
        });

        var notificationPipelineBehaviorTypes = notificationBehaviors
            .Where(nb => usedNotifications.Contains(nb.NotificationInfo.ActualNotification))
            .SelectMany(nb =>
            {
                var actualNotif = nb.NotificationInfo.ActualNotification;
                return nb.Behaviors.Select(b =>
                    $"typeof({b.Class.ToDisplayString(_fqn).DropGenerics()}<{actualNotif.ToDisplayString(_fqn)}>)");
            });

        // Generate the complete SwitchMediator class
        return Normalize(
            $$"""
               //------------------------------------------------------------------------------
               // <auto-generated>
               //     This code was generated by SwitchMediator.
               //
               //     Changes to this file may cause incorrect behavior and will be lost if
               //     the code is regenerated.
               // </auto-generated>
               //------------------------------------------------------------------------------
               
               #nullable enable
               
               using System;
               using System.Linq;
               using System.Collections.Generic;
               using System.Diagnostics;
               using System.Runtime.CompilerServices;
               using System.Threading;
               using System.Threading.Tasks;
               
               #if NET8_0_OR_GREATER
               using System.Collections.Frozen;
               #endif
               
               namespace Mediator.Switch;
               
               #pragma warning disable CS1998
               
               internal class SwitchMediator : IMediator
               {
                   #region Fields
               
                   {{string.Join("\n    ", handlerFields.Concat(behaviorFields).Concat(notificationBehaviorFields).Concat(notificationHandlerFields))}}
               
                   private readonly ISwitchMediatorServiceProvider _svc;
               
                   #endregion
               
                   #region Constructor
               
                   public SwitchMediator(ISwitchMediatorServiceProvider serviceProvider)
                   {
                       _svc = serviceProvider;
                   }
               
                   #endregion
               
                   public static (global::System.Collections.Generic.IReadOnlyList<global::System.Type> RequestHandlerTypes, global::System.Collections.Generic.IReadOnlyList<(global::System.Type NotificationType, global::System.Collections.Generic.IReadOnlyList<global::System.Type> HandlerTypes)> NotificationTypes, global::System.Collections.Generic.IReadOnlyList<global::System.Type> PipelineBehaviorTypes) KnownTypes
                   {
                       get { return (SwitchMediatorKnownTypes.RequestHandlerTypes, SwitchMediatorKnownTypes.NotificationTypes, SwitchMediatorKnownTypes.PipelineBehaviorTypes); }
                   }
               
                   public global::System.Threading.Tasks.Task<TResponse> Send<TResponse>(IRequest<TResponse> request, global::System.Threading.CancellationToken cancellationToken = default)
                   {
                       if (SendSwitchCase.Cases.TryGetValue(request.GetType(), out var handle))
                       {
                           return (global::System.Threading.Tasks.Task<TResponse>)handle(this, request, cancellationToken);
                       }
                       
                       throw new global::System.ArgumentException($"No handler for {request.GetType().Name}");
                   }
               
                   private static class SendSwitchCase
                   {
                       public static readonly global::System.Collections.Generic.IDictionary<global::System.Type, global::System.Func<SwitchMediator, object, global::System.Threading.CancellationToken, object>> Cases = new (global::System.Type, global::System.Func<SwitchMediator, object, global::System.Threading.CancellationToken, object>)[]
                       {
               {{string.Join(",\n", sendCases)}}
                       }
               #if NET8_0_OR_GREATER
                       .ToFrozenDictionary
               #else
                       .ToDictionary
               #endif
                           (t => t.Item1, t => t.Item2);
                   }
               
                   public global::System.Threading.Tasks.Task Publish(INotification notification, global::System.Threading.CancellationToken cancellationToken = default)
                   {
                       if (PublishSwitchCase.Cases.TryGetValue(notification.GetType(), out var handle))
                       {
                           return handle(this, notification, cancellationToken);
                       }
                       
                       return global::System.Threading.Tasks.Task.CompletedTask;
                   }
               
                   private static class PublishSwitchCase
                   {
                       public static readonly global::System.Collections.Generic.IDictionary<global::System.Type, global::System.Func<SwitchMediator, INotification, global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task>> Cases = new (global::System.Type, global::System.Func<SwitchMediator, INotification, global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task>)[]
                       {
               {{string.Join(",\n", publishCases)}}
                       }
               #if NET8_0_OR_GREATER
                       .ToFrozenDictionary
               #else
                       .ToDictionary
               #endif
                           (t => t.Item1, t => t.Item2);
                   }
               
                   {{string.Join("\n\n    ", behaviorMethods)}}
               
                   [MethodImpl(MethodImplOptions.AggressiveInlining)]
                   [DebuggerStepThrough]
                   private T Get<T>(ref T? field) where T : notnull
                   {
                       return field ?? (field = _svc.Get<T>());
                   }
               
                   /// <summary>
                   /// Provides lists of SwitchMediator component implementation types.
                   /// </summary>
                   public static class SwitchMediatorKnownTypes
                   {
                       public static readonly global::System.Collections.Generic.IReadOnlyList<global::System.Type> RequestHandlerTypes =
                           new global::System.Type[] {
                               {{string.Join(",\n                ", requestHandlerTypes)}}
                           }.AsReadOnly();
                   
                       public static readonly global::System.Collections.Generic.IReadOnlyList<(global::System.Type NotificationType, global::System.Collections.Generic.IReadOnlyList<global::System.Type> HandlerTypes)> NotificationTypes = 
                          new (global::System.Type NotificationType, global::System.Collections.Generic.IReadOnlyList<global::System.Type> HandlerTypes)[] {
                               {{string.Join(",\n                ", notificationTypes)}}
                          }.AsReadOnly();
               
                       public static readonly global::System.Collections.Generic.IReadOnlyList<global::System.Type> PipelineBehaviorTypes =
                          new global::System.Type[] {
                               {{string.Join(",\n                ", pipelineBehaviorTypes.Concat(notificationPipelineBehaviorTypes))}}
                          }.AsReadOnly();
                   }    
               }
               """);
    }

    private static ITypeSymbol? TryGetActualRequest(
        ITypeSymbol iRequestType,
        List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse)> handlers,
        (INamedTypeSymbol Class, ITypeSymbol TResponse) request)
    {
        var current = request.Class;
        do
        {
            var handler = handlers.FirstOrDefault(h =>
                h.TRequest.Equals(current, SymbolEqualityComparer.Default));
            if (handler != default)
            {
                return current;
            }
            current = current.BaseType;
        } while (current != null &&
                 current.AllInterfaces.Any(i =>
                     SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, iRequestType)));

        return null;
    }

    private static string GenerateSendCase(
        ITypeSymbol actualHandler,
        (INamedTypeSymbol Class, ITypeSymbol TResponse) request) =>
        $$"""
                      ( // case {{request.Class}}:
                          typeof({{request.Class.ToDisplayString(_fqn)}}), (instance, request, cancellationToken) =>
                              instance.Handle_{{actualHandler.GetVariableName(false)}}(
                                  ({{request.Class.ToDisplayString(_fqn)}}) request, cancellationToken)
                      )
          """;

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

    private static string GeneratePublishCase(
        ITypeSymbol actualNotification,
        ITypeSymbol notification,
        List<((ITypeSymbol Notification, ITypeSymbol ActualNotification) NotificationInfo, List<(INamedTypeSymbol Class, ITypeSymbol TNotification, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> Behaviors)> notificationBehaviors)
    {
        var behaviors = notificationBehaviors
            .FirstOrDefault(nb => SymbolEqualityComparer.Default.Equals(nb.NotificationInfo.ActualNotification, actualNotification))
            .Behaviors ?? new List<(INamedTypeSymbol Class, ITypeSymbol TNotification, IReadOnlyList<ITypeParameterSymbol> TypeParameters)>();

        var notificationName = actualNotification.GetVariableName();
        var notificationType = actualNotification.ToDisplayString(_fqn);

        var behaviorResolutions = behaviors.Select(b =>
            $"var {b.Class.GetVariableName()}__{notificationName} = instance.Get(ref instance._{b.Class.GetVariableName()}__{notificationName});");

        var typedVar = "typedNotification";
        var coreCall = $"handler.Handle({typedVar}, cancellationToken)";
        var chain = BehaviorChainBuilder.BuildNotification(behaviors, notificationName, typedVar, coreCall);

        return $$"""
                      ( // case {{notification}}:
                          typeof({{notification.ToDisplayString(_fqn)}}), async (instance, notification, cancellationToken) =>
                          {
                              var handlers = instance.Get(ref instance._{{actualNotification.GetVariableName()}}__Handlers);
                              {{string.Join("\n                    ", behaviorResolutions)}}
                              var {{typedVar}} = ({{notificationType}}) notification;
                              
                              foreach (var handler in handlers)
                              {
                                  await
                                      {{chain}};
                              }
                          }
                      )
          """;
    }

    private static string? TryGenerateBehaviorMethod(List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse)> handlers,
        ((INamedTypeSymbol Class, ITypeSymbol TResponse) Request, List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> Behaviors) r)
    {
        var (request, applicableBehaviors) = r;
        var handler = handlers.FirstOrDefault(h => h.TRequest.Equals(request.Class, SymbolEqualityComparer.Default));
        if (handler == default) return null;
        var requestName = request.Class.GetVariableName();
        var chainVars = applicableBehaviors.Select(behavior => $"var {behavior.Class.GetVariableName()}__{requestName} = Get(ref _{behavior.Class.GetVariableName()}__{requestName});");
        var coreVar = $"var {handler.Class.GetVariableName()} = Get(ref _{handler.Class.GetVariableName()});";

        var coreCall = $"{handler.Class.GetVariableName()}.Handle(request, cancellationToken)";
        var chain = BehaviorChainBuilder.BuildRequest(applicableBehaviors, requestName, coreCall);

        return $$"""
                 private global::System.Threading.Tasks.Task<{{request.TResponse.ToDisplayString(_fqn)}}> Handle_{{request.Class.GetVariableName(false)}}(
                         {{request.Class.ToDisplayString(_fqn)}} request,
                         global::System.Threading.CancellationToken cancellationToken)
                     {
                         {{string.Join("\n        ", chainVars)}}
                         {{coreVar}}
                         
                         return
                             {{chain}};
                     }
                 """;
    }

    private static string Normalize(string code) =>
        string.Join("\n",
            code.Replace("\r\n", "\n")
                .Split('\n')
                .Select(line => line.TrimEnd()));
}