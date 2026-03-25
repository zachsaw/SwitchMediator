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
        var mediator = analysis.MediatorClass ?? throw new ArgumentNullException(nameof(analysis.MediatorClass));
        var className = mediator.Name;
        var namespaceName = mediator.ContainingNamespace.IsGlobalNamespace
            ? null
            : mediator.ContainingNamespace.ToDisplayString();
        var accessibility = mediator.DeclaredAccessibility.ToString().ToLowerInvariant();

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

        // Generate notification handler fields (Task and ValueTask variants)
        var notificationHandlerFields = usedNotifications.SelectMany(n =>
        {
            var fields = new List<string>();
            var handlersForN = notificationHandlers
                .Where(h => h.TNotification.Equals(n, SymbolEqualityComparer.Default))
                .ToList();
            if (handlersForN.Any(h => !h.IsValueTask))
                fields.Add($"private global::System.Collections.Generic.IEnumerable<global::Mediator.Switch.INotificationHandler<{n.ToDisplayString(_fqn)}>>? _{n.GetVariableName()}__Handlers;");
            if (handlersForN.Any(h => h.IsValueTask))
                fields.Add($"private global::System.Collections.Generic.IEnumerable<global::Mediator.Switch.IValueNotificationHandler<{n.ToDisplayString(_fqn)}>>? _{n.GetVariableName()}__ValueHandlers;");
            return fields;
        });

        // Generate Task Send method switch cases
        var taskSendCases = actualSendCases.Select(c =>
        {
            var handlerIsValueTask = handlers.FirstOrDefault(h => h.TRequest.Equals(c.ActualRequest, SymbolEqualityComparer.Default)).IsValueTask;
            return GenerateTaskSendCase(c.ActualRequest, c.Request, handlerIsValueTask, className);
        });

        // Generate ValueTask Send method switch cases
        var valueSendCases = actualSendCases.Select(c =>
        {
            var handlerIsValueTask = handlers.FirstOrDefault(h => h.TRequest.Equals(c.ActualRequest, SymbolEqualityComparer.Default)).IsValueTask;
            return GenerateValueSendCase(c.ActualRequest, c.Request, handlerIsValueTask, className);
        });

        // Generate behavior chain methods for requests
        var behaviorMethods = actualRequestBehaviors
            .Select(r => TryGenerateBehaviorMethod(handlers, r))
            .Where(m => m != null);

        // Generate Task Publish method switch cases (Task-based notification handlers only)
        var taskPublishCases = actualPublishCases
            .Select(n => GenerateTaskPublishCase(n.ActualNotification, n.Notification, notificationHandlers, notificationBehaviors))
            .Where(c => c != null);

        // Generate ValueTask Publish method switch cases (ValueTask-based notification handlers only)
        var valuePublishCases = actualPublishCases
            .Select(n => GenerateValuePublishCase(n.ActualNotification, n.Notification, notificationHandlers, notificationBehaviors))
            .Where(c => c != null);

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

        // Generate pipelined handler wrappers — one per handled request
        // These implement IRequestHandler<TRequest,TResponse> and IValueRequestHandler<TRequest,TResponse>
        // so that the full pipeline runs when the handler is injected directly from DI.
        var pipelinedHandlers = handlers
            .Where(h => usedRequests.Contains(h.TRequest, SymbolEqualityComparer.Default))
            .ToList();

        var pipelinedHandlerClasses = pipelinedHandlers
            .Select(h =>
            {
                var entry = actualRequestBehaviors.First(r => SymbolEqualityComparer.Default.Equals(r.Request.Class, h.TRequest));
                return GeneratePipelinedHandlerClass(h, entry.Behaviors);
            });

        var pipelinedHandlerTypeEntries = pipelinedHandlers
            .Select(h =>
            {
                var requestFqn = h.TRequest.ToDisplayString(_fqn);
                var responseFqn = h.TResponse.ToDisplayString(_fqn);
                var wrapperClassName = $"{className}.PipelinedHandler_{h.TRequest.GetVariableName(false)}";
                return $"(typeof(global::Mediator.Switch.IRequestHandler<{requestFqn}, {responseFqn}>), typeof({wrapperClassName}), typeof(global::Mediator.Switch.IValueRequestHandler<{requestFqn}, {responseFqn}>))";
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
               
               {{(namespaceName != null ? $"namespace {namespaceName};" : "")}}
               
               #pragma warning disable CS1998
               
               {{accessibility}} partial class {{className}} : global::Mediator.Switch.IMediator, global::Mediator.Switch.IValueMediator
               {
                   #region Fields
               
                   {{string.Join("\n    ", handlerFields.Concat(behaviorFields).Concat(notificationBehaviorFields).Concat(notificationHandlerFields))}}
               
                   private readonly global::Mediator.Switch.ISwitchMediatorServiceProvider _svc;
               
                   #endregion
               
                   #region Constructor
               
                   public {{className}}(global::Mediator.Switch.ISwitchMediatorServiceProvider serviceProvider)
                   {
                       _svc = serviceProvider;
                   }
               
                   #endregion
               
                   public static (global::System.Collections.Generic.IReadOnlyList<global::System.Type> RequestHandlerTypes, global::System.Collections.Generic.IReadOnlyList<(global::System.Type NotificationType, global::System.Collections.Generic.IReadOnlyList<global::System.Type> HandlerTypes)> NotificationTypes, global::System.Collections.Generic.IReadOnlyList<global::System.Type> PipelineBehaviorTypes) KnownTypes
                   {
                       get { return (SwitchMediatorKnownTypes.RequestHandlerTypes, SwitchMediatorKnownTypes.NotificationTypes, SwitchMediatorKnownTypes.PipelineBehaviorTypes); }
                   }
               
                   public static global::System.Collections.Generic.IReadOnlyList<(global::System.Type RequestHandlerInterfaceType, global::System.Type PipelinedHandlerType, global::System.Type ValueRequestHandlerInterfaceType)> PipelinedHandlerTypes
                   {
                       get { return SwitchMediatorKnownTypes.PipelinedHandlerTypes; }
                   }
               
                   public global::System.Threading.Tasks.Task<TResponse> Send<TResponse>(global::Mediator.Switch.IRequest<TResponse> request, global::System.Threading.CancellationToken cancellationToken = default)
                   {
                       if (TaskSendSwitchCase.Cases.TryGetValue(request.GetType(), out var handle))
                       {
                           return global::System.Runtime.CompilerServices.Unsafe.As<global::System.Func<{{className}}, global::Mediator.Switch.IRequest<TResponse>, global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task<TResponse>>>(handle)(this, request, cancellationToken);
                       }
                       
                       throw new global::System.ArgumentException($"No handler for {request.GetType().Name}");
                   }
               
                   private static class TaskSendSwitchCase
                   {
                       public static readonly global::System.Collections.Generic.IDictionary<global::System.Type, object> Cases = new (global::System.Type, object)[]
                       {
               {{string.Join(",\n", taskSendCases)}}
                       }
               #if NET8_0_OR_GREATER
                       .ToFrozenDictionary
               #else
                       .ToDictionary
               #endif
                           (t => t.Item1, t => t.Item2);
                   }
               
                   global::System.Threading.Tasks.ValueTask<TResponse> global::Mediator.Switch.IValueSender.Send<TResponse>(global::Mediator.Switch.IRequest<TResponse> request, global::System.Threading.CancellationToken cancellationToken)
                   {
                       if (ValueSendSwitchCase.Cases.TryGetValue(request.GetType(), out var handle))
                       {
                           return global::System.Runtime.CompilerServices.Unsafe.As<global::System.Func<{{className}}, global::Mediator.Switch.IRequest<TResponse>, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask<TResponse>>>(handle)(this, request, cancellationToken);
                       }
                       
                       throw new global::System.ArgumentException($"No handler for {request.GetType().Name}");
                   }
               
                   private static class ValueSendSwitchCase
                   {
                       public static readonly global::System.Collections.Generic.IDictionary<global::System.Type, object> Cases = new (global::System.Type, object)[]
                       {
               {{string.Join(",\n", valueSendCases)}}
                       }
               #if NET8_0_OR_GREATER
                       .ToFrozenDictionary
               #else
                       .ToDictionary
               #endif
                           (t => t.Item1, t => t.Item2);
                   }
               
                   public global::System.Threading.Tasks.Task Publish(global::Mediator.Switch.INotification notification, global::System.Threading.CancellationToken cancellationToken = default)
                   {
                       if (TaskPublishSwitchCase.Cases.TryGetValue(notification.GetType(), out var handle))
                       {
                           return handle(this, notification, cancellationToken);
                       }
                       
                       if (ValuePublishSwitchCase.Cases.TryGetValue(notification.GetType(), out var valueHandle))
                       {
                           return valueHandle(this, notification, cancellationToken).AsTask();
                       }
                       
                       return global::System.Threading.Tasks.Task.CompletedTask;
                   }
               
                   private static class TaskPublishSwitchCase
                   {
                       public static readonly global::System.Collections.Generic.IDictionary<global::System.Type, global::System.Func<{{className}}, global::Mediator.Switch.INotification, global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task>> Cases = new (global::System.Type, global::System.Func<{{className}}, global::Mediator.Switch.INotification, global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task>)[]
                       {
               {{string.Join(",\n", taskPublishCases)}}
                       }
               #if NET8_0_OR_GREATER
                       .ToFrozenDictionary
               #else
                       .ToDictionary
               #endif
                           (t => t.Item1, t => t.Item2);
                   }
               
                   global::System.Threading.Tasks.ValueTask global::Mediator.Switch.IValuePublisher.Publish(global::Mediator.Switch.INotification notification, global::System.Threading.CancellationToken cancellationToken)
                   {
                       if (ValuePublishSwitchCase.Cases.TryGetValue(notification.GetType(), out var handle))
                       {
                           return handle(this, notification, cancellationToken);
                       }
                       
                       if (TaskPublishSwitchCase.Cases.TryGetValue(notification.GetType(), out var taskHandle))
                       {
                           return new global::System.Threading.Tasks.ValueTask(taskHandle(this, notification, cancellationToken));
                       }
                       
                       return global::System.Threading.Tasks.ValueTask.CompletedTask;
                   }
               
                   private static class ValuePublishSwitchCase
                   {
                       public static readonly global::System.Collections.Generic.IDictionary<global::System.Type, global::System.Func<{{className}}, global::Mediator.Switch.INotification, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask>> Cases = new (global::System.Type, global::System.Func<{{className}}, global::Mediator.Switch.INotification, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask>)[]
                       {
               {{string.Join(",\n", valuePublishCases)}}
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
               
                       public static readonly global::System.Collections.Generic.IReadOnlyList<(global::System.Type RequestHandlerInterfaceType, global::System.Type PipelinedHandlerType, global::System.Type ValueRequestHandlerInterfaceType)> PipelinedHandlerTypes =
                          new (global::System.Type, global::System.Type, global::System.Type)[] {
                               {{string.Join(",\n                ", pipelinedHandlerTypeEntries)}}
                          }.AsReadOnly();
                   }

                   #region Pipelined Handler Wrappers

                   {{string.Join("\n\n    ", pipelinedHandlerClasses)}}

                   #endregion
               }
               """);
    }

    private static ITypeSymbol? TryGetActualRequest(
        ITypeSymbol iRequestType,
        List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, bool IsValueTask)> handlers,
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

    private static string GenerateTaskSendCase(
        ITypeSymbol actualHandler,
        (INamedTypeSymbol Class, ITypeSymbol TResponse) request,
        bool handlerIsValueTask,
        string className) =>
        handlerIsValueTask
            // ValueTask handler → wrap with .AsTask() for Task send path
            ? $$"""
                      ( // case {{request.Class}}:
                          typeof({{request.Class.ToDisplayString(_fqn)}}), (global::System.Func<{{className}}, global::Mediator.Switch.IRequest<{{request.TResponse.ToDisplayString(_fqn)}}>, global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task<{{request.TResponse.ToDisplayString(_fqn)}}>>) ((instance, request, cancellationToken) =>
                              instance.Handle_{{actualHandler.GetVariableName(false)}}(
                                  ({{request.Class.ToDisplayString(_fqn)}}) request, cancellationToken).AsTask())
                      )
          """
            // Task handler → return directly
            : $$"""
                      ( // case {{request.Class}}:
                          typeof({{request.Class.ToDisplayString(_fqn)}}), (global::System.Func<{{className}}, global::Mediator.Switch.IRequest<{{request.TResponse.ToDisplayString(_fqn)}}>, global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task<{{request.TResponse.ToDisplayString(_fqn)}}>>) ((instance, request, cancellationToken) =>
                              instance.Handle_{{actualHandler.GetVariableName(false)}}(
                                  ({{request.Class.ToDisplayString(_fqn)}}) request, cancellationToken))
                      )
          """;

    private static string GenerateValueSendCase(
        ITypeSymbol actualHandler,
        (INamedTypeSymbol Class, ITypeSymbol TResponse) request,
        bool handlerIsValueTask,
        string className) =>
        handlerIsValueTask
            // ValueTask handler → return directly (zero allocation)
            ? $$"""
                      ( // case {{request.Class}}:
                          typeof({{request.Class.ToDisplayString(_fqn)}}), (global::System.Func<{{className}}, global::Mediator.Switch.IRequest<{{request.TResponse.ToDisplayString(_fqn)}}>, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask<{{request.TResponse.ToDisplayString(_fqn)}}>>) ((instance, request, cancellationToken) =>
                              instance.Handle_{{actualHandler.GetVariableName(false)}}(
                                  ({{request.Class.ToDisplayString(_fqn)}}) request, cancellationToken))
                      )
          """
            // Task handler → wrap in ValueTask struct
            : $$"""
                      ( // case {{request.Class}}:
                          typeof({{request.Class.ToDisplayString(_fqn)}}), (global::System.Func<{{className}}, global::Mediator.Switch.IRequest<{{request.TResponse.ToDisplayString(_fqn)}}>, global::System.Threading.CancellationToken, global::System.Threading.Tasks.ValueTask<{{request.TResponse.ToDisplayString(_fqn)}}>>) ((instance, request, cancellationToken) =>
                              new global::System.Threading.Tasks.ValueTask<{{request.TResponse.ToDisplayString(_fqn)}}>(
                                  instance.Handle_{{actualHandler.GetVariableName(false)}}(
                                      ({{request.Class.ToDisplayString(_fqn)}}) request, cancellationToken)))
                      )
          """;

    private static ITypeSymbol? TryGetActualNotification(
        ITypeSymbol iNotificationType,
        List<(INamedTypeSymbol Class, ITypeSymbol TNotification, bool IsValueTask)> notificationHandlers,
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

    private static string? GenerateTaskPublishCase(
        ITypeSymbol actualNotification,
        ITypeSymbol notification,
        List<(INamedTypeSymbol Class, ITypeSymbol TNotification, bool IsValueTask)> notificationHandlers,
        List<((ITypeSymbol Notification, ITypeSymbol ActualNotification) NotificationInfo, List<(INamedTypeSymbol Class, ITypeSymbol TNotification, IReadOnlyList<ITypeParameterSymbol> TypeParameters, bool IsValueTask)> Behaviors)> notificationBehaviors)
    {
        // Only generate Task publish case for Task-based notification handlers
        var handlersForNotification = notificationHandlers
            .Where(h => h.TNotification.Equals(actualNotification, SymbolEqualityComparer.Default))
            .ToList();
        if (handlersForNotification.Any(h => h.IsValueTask) && !handlersForNotification.Any(h => !h.IsValueTask))
            return null; // All handlers are ValueTask, skip Task publish case

        var behaviors = notificationBehaviors
            .FirstOrDefault(nb => SymbolEqualityComparer.Default.Equals(nb.NotificationInfo.ActualNotification, actualNotification))
            .Behaviors ?? new List<(INamedTypeSymbol Class, ITypeSymbol TNotification, IReadOnlyList<ITypeParameterSymbol> TypeParameters, bool IsValueTask)>();

        // Use only Task-based behaviors
        var taskBehaviors = behaviors.Where(b => !b.IsValueTask).ToList();

        var notificationName = actualNotification.GetVariableName();
        var notificationType = actualNotification.ToDisplayString(_fqn);

        var behaviorResolutions = taskBehaviors.Select(b =>
            $"var {b.Class.GetVariableName()}__{notificationName} = instance.Get(ref instance._{b.Class.GetVariableName()}__{notificationName});");

        var typedVar = "typedNotification";
        var coreCall = $"handler.Handle({typedVar}, cancellationToken)";
        var chain = BehaviorChainBuilder.BuildNotification(taskBehaviors, notificationName, typedVar, coreCall);

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

    private static string? GenerateValuePublishCase(
        ITypeSymbol actualNotification,
        ITypeSymbol notification,
        List<(INamedTypeSymbol Class, ITypeSymbol TNotification, bool IsValueTask)> notificationHandlers,
        List<((ITypeSymbol Notification, ITypeSymbol ActualNotification) NotificationInfo, List<(INamedTypeSymbol Class, ITypeSymbol TNotification, IReadOnlyList<ITypeParameterSymbol> TypeParameters, bool IsValueTask)> Behaviors)> notificationBehaviors)
    {
        // Only generate ValueTask publish case for ValueTask-based notification handlers
        var handlersForNotification = notificationHandlers
            .Where(h => h.TNotification.Equals(actualNotification, SymbolEqualityComparer.Default))
            .ToList();
        if (!handlersForNotification.Any(h => h.IsValueTask))
            return null; // No ValueTask handlers, skip ValueTask publish case

        var behaviors = notificationBehaviors
            .FirstOrDefault(nb => SymbolEqualityComparer.Default.Equals(nb.NotificationInfo.ActualNotification, actualNotification))
            .Behaviors ?? new List<(INamedTypeSymbol Class, ITypeSymbol TNotification, IReadOnlyList<ITypeParameterSymbol> TypeParameters, bool IsValueTask)>();

        // Use only ValueTask-based behaviors
        var valueBehaviors = behaviors.Where(b => b.IsValueTask).ToList();

        var notificationName = actualNotification.GetVariableName();
        var notificationType = actualNotification.ToDisplayString(_fqn);

        var behaviorResolutions = valueBehaviors.Select(b =>
            $"var {b.Class.GetVariableName()}__{notificationName} = instance.Get(ref instance._{b.Class.GetVariableName()}__{notificationName});");

        var typedVar = "typedNotification";
        var coreCall = $"handler.Handle({typedVar}, cancellationToken)";
        var chain = BehaviorChainBuilder.BuildNotification(valueBehaviors, notificationName, typedVar, coreCall);

        return $$"""
                      ( // case {{notification}}:
                          typeof({{notification.ToDisplayString(_fqn)}}), async (instance, notification, cancellationToken) =>
                          {
                              var handlers = instance.Get(ref instance._{{actualNotification.GetVariableName()}}__ValueHandlers);
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

    private static string? TryGenerateBehaviorMethod(List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, bool IsValueTask)> handlers,
        ((INamedTypeSymbol Class, ITypeSymbol TResponse) Request, List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters, bool IsValueTask)> Behaviors) r)
    {
        var (request, applicableBehaviors) = r;
        var handler = handlers.FirstOrDefault(h => h.TRequest.Equals(request.Class, SymbolEqualityComparer.Default));
        if (handler == default) return null;
        var requestName = request.Class.GetVariableName();
        var chainVars = applicableBehaviors.Select(behavior => $"var {behavior.Class.GetVariableName()}__{requestName} = Get(ref _{behavior.Class.GetVariableName()}__{requestName});");
        var coreVar = $"var {handler.Class.GetVariableName()} = Get(ref _{handler.Class.GetVariableName()});";

        var coreCall = $"{handler.Class.GetVariableName()}.Handle(request, cancellationToken)";
        var chain = BehaviorChainBuilder.BuildRequest(applicableBehaviors, requestName, coreCall);

        var returnType = handler.IsValueTask
            ? $"global::System.Threading.Tasks.ValueTask<{request.TResponse.ToDisplayString(_fqn)}>"
            : $"global::System.Threading.Tasks.Task<{request.TResponse.ToDisplayString(_fqn)}>";

        return $$"""
                 private {{returnType}} Handle_{{request.Class.GetVariableName(false)}}(
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

    private static string GeneratePipelinedHandlerClass(
        (INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, bool IsValueTask) handler,
        List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters, bool IsValueTask)> applicableBehaviors)
    {
        var requestFqn = handler.TRequest.ToDisplayString(_fqn);
        var responseFqn = handler.TResponse.ToDisplayString(_fqn);
        var requestName = handler.TRequest.GetVariableName();
        var requestVarNamePascal = handler.TRequest.GetVariableName(false);
        var innerClassName = $"PipelinedHandler_{requestVarNamePascal}";

        // Native return type of HandleCore matches the handler's async pattern
        var nativeReturnType = handler.IsValueTask
            ? $"global::System.Threading.Tasks.ValueTask<{responseFqn}>"
            : $"global::System.Threading.Tasks.Task<{responseFqn}>";

        // Fields — one per behavior (closed generic) + one for the raw handler
        var behaviorFields = applicableBehaviors.Select(b =>
            $"private readonly {b.Class.ToDisplayString(_fqn).DropGenerics()}<{requestFqn}, {b.TResponse.ToDisplayString(_fqn)}> _{b.Class.GetVariableName()}__{requestName};");
        var handlerField = $"private readonly {handler.Class.ToDisplayString(_fqn)} _{handler.Class.GetVariableName()};";

        // Constructor parameters
        var ctorBehaviorParams = applicableBehaviors.Select(b =>
            $"{b.Class.ToDisplayString(_fqn).DropGenerics()}<{requestFqn}, {b.TResponse.ToDisplayString(_fqn)}> {b.Class.GetVariableName()}__{requestName}");
        var ctorHandlerParam = $"{handler.Class.ToDisplayString(_fqn)} {handler.Class.GetVariableName()}";
        var ctorParams = ctorBehaviorParams.Append(ctorHandlerParam);

        // Constructor body — assign each parameter to its backing field
        var ctorBehaviorAssignments = applicableBehaviors.Select(b =>
            $"_{b.Class.GetVariableName()}__{requestName} = {b.Class.GetVariableName()}__{requestName};");
        var ctorHandlerAssignment = $"_{handler.Class.GetVariableName()} = {handler.Class.GetVariableName()};";
        var ctorAssignments = ctorBehaviorAssignments.Append(ctorHandlerAssignment);

        // HandleCore body — local aliases from fields, then the russian doll chain
        // (same pattern as TryGenerateBehaviorMethod but self-contained; 16-space continuation to
        // align with the chain being placed at 16 relative spaces inside HandleCore's body)
        var behaviorAliases = applicableBehaviors.Select(b =>
            $"var {b.Class.GetVariableName()}__{requestName} = _{b.Class.GetVariableName()}__{requestName};");
        var handlerAlias = $"var {handler.Class.GetVariableName()} = _{handler.Class.GetVariableName()};";
        var coreBodyVarLines = behaviorAliases.Append(handlerAlias);

        var coreCall = $"{handler.Class.GetVariableName()}.Handle(request, cancellationToken)";
        var chain = BehaviorChainBuilder.BuildRequest(applicableBehaviors, requestName, coreCall, "                ");

        // IRequestHandler<T,R>.Handle: if the pipeline is Task-native → direct; if ValueTask-native → .AsTask()
        var taskHandleBody = handler.IsValueTask
            ? "HandleCore(request, cancellationToken).AsTask()"
            : "HandleCore(request, cancellationToken)";

        // IValueRequestHandler<T,R>.Handle: if the pipeline is Task-native → new ValueTask<T>(); if ValueTask-native → direct
        var valueTaskHandleBody = handler.IsValueTask
            ? "HandleCore(request, cancellationToken)"
            : $"new global::System.Threading.Tasks.ValueTask<{responseFqn}>(HandleCore(request, cancellationToken))";

        var allFields = behaviorFields.Append(handlerField);

        return $$"""
                 public sealed class {{innerClassName}} :
                         global::Mediator.Switch.IRequestHandler<{{requestFqn}}, {{responseFqn}}>,
                         global::Mediator.Switch.IValueRequestHandler<{{requestFqn}}, {{responseFqn}}>
                     {
                         {{string.Join("\n        ", allFields)}}
                 
                         public {{innerClassName}}({{string.Join(", ", ctorParams)}})
                         {
                             {{string.Join("\n            ", ctorAssignments)}}
                         }
                 
                         private {{nativeReturnType}} HandleCore(
                             {{requestFqn}} request,
                             global::System.Threading.CancellationToken cancellationToken)
                         {
                             {{string.Join("\n            ", coreBodyVarLines)}}
                 
                             return
                                 {{chain}};
                         }
                 
                         global::System.Threading.Tasks.Task<{{responseFqn}}> global::Mediator.Switch.IRequestHandler<{{requestFqn}}, {{responseFqn}}>.Handle(
                             {{requestFqn}} request, global::System.Threading.CancellationToken cancellationToken)
                             => {{taskHandleBody}};
                 
                         global::System.Threading.Tasks.ValueTask<{{responseFqn}}> global::Mediator.Switch.IValueRequestHandler<{{requestFqn}}, {{responseFqn}}>.Handle(
                             {{requestFqn}} request, global::System.Threading.CancellationToken cancellationToken)
                             => {{valueTaskHandleBody}};
                     }
                 """;
    }

    private static string Normalize(string code) =>
        string.Join("\n",
            code.Replace("\r\n", "\n")
                .Split('\n')
                .Select(line => line.TrimEnd()));
}