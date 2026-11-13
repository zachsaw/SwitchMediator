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
        var cacheMode = analysis.HandlerCacheMode;
        var useFields = cacheMode == HandlerCacheMode.Fields;

        // How the mediator obtains a component from the container. "Direct" takes System.IServiceProvider
        // straight from the scope, removing the ISwitchMediatorServiceProvider service and its allocation.
        var serviceProviderType = analysis.ServiceProviderMode == MediatorServiceProviderMode.Direct
            ? "global::System.IServiceProvider"
            : "global::Mediator.Switch.ISwitchMediatorServiceProvider";
        var resolveExpression = analysis.ServiceProviderMode == MediatorServiceProviderMode.Direct
            ? "(T)(_svc.GetService(typeof(T)) ?? throw new global::System.InvalidOperationException($\"No service for type '{typeof(T)}' has been registered.\"))"
            : "_svc.Get<T>()";

        // The cache accessor emitted into the mediator, one flavour per HandlerCache mode.
        // Continuation lines are padded by one member level because the block is interpolated
        // after the template's own indentation, which only applies to its first line.
        var cacheAccessor = Indent(cacheMode switch
        {
            HandlerCacheMode.Fields => string.Join("\n",
                "[MethodImpl(MethodImplOptions.AggressiveInlining)]",
                "[DebuggerStepThrough]",
                "private T Get<T>(ref T? field) where T : notnull",
                "{",
                $"    return field ?? (field = {resolveExpression});",
                "}"),
            HandlerCacheMode.Dictionary => string.Join("\n",
                "[MethodImpl(MethodImplOptions.AggressiveInlining)]",
                "[DebuggerStepThrough]",
                "private T Get<T>() where T : notnull",
                "{",
                "    var cache = _cache;",
                "    if (cache.TryGetValue(typeof(T), out var cached))",
                "    {",
                "        return (T)cached!;",
                "    }",
                $"    var resolved = {resolveExpression};",
                "    cache[typeof(T)] = resolved;",
                "    return resolved;",
                "}"),
            HandlerCacheMode.LazyDictionary => string.Join("\n",
                "[MethodImpl(MethodImplOptions.AggressiveInlining)]",
                "[DebuggerStepThrough]",
                "private T Get<T>() where T : notnull",
                "{",
                "    var cache = _cache ??= new global::System.Collections.Generic.Dictionary<global::System.Type, object?>();",
                "    if (cache.TryGetValue(typeof(T), out var cached))",
                "    {",
                "        return (T)cached!;",
                "    }",
                $"    var resolved = {resolveExpression};",
                "    cache[typeof(T)] = resolved;",
                "    return resolved;",
                "}"),
            _ => string.Join("\n",
                "[MethodImpl(MethodImplOptions.AggressiveInlining)]",
                "[DebuggerStepThrough]",
                "private T Get<T>() where T : notnull",
                "{",
                $"    return {resolveExpression};",
                "}"),
        });

        static string Indent(string block) =>
            string.Join("\n", block.Split('\n').Select((line, index) => index == 0 ? line : "    " + line));

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
        var handlerFields = useFields
            ? handlers
                .Select(h => h.Class)
                .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
                .Select(cls => $"private {cls.ToDisplayString(_fqn)}? _{cls.GetVariableName()};")
            : Enumerable.Empty<string>();

        // Generate behavior fields specific to each request
        var behaviorFields = !useFields
            ? Enumerable.Empty<string>()
            : actualRequestBehaviors.SelectMany(r =>
            {
                var (request, applicableBehaviors) = r;
                return applicableBehaviors.Select(b =>
                    $"private {b.Class.ToDisplayString(_fqn).DropGenerics()}<{request.Class.ToDisplayString(_fqn)}, {b.TResponse.ToDisplayString(_fqn)}>? _{b.Class.GetVariableName()}__{request.Class.GetVariableName()};");
            });

        // Generate behavior fields specific to each notification
        var notificationBehaviorFields = !useFields
            ? Enumerable.Empty<string>()
            : notificationBehaviors
                .Where(nb => usedNotifications.Contains(nb.NotificationInfo.ActualNotification))
                .SelectMany(nb =>
                {
                    var actualNotif = nb.NotificationInfo.ActualNotification;
                    return nb.Behaviors.Select(b =>
                        $"private {b.Class.ToDisplayString(_fqn).DropGenerics()}<{actualNotif.ToDisplayString(_fqn)}>? _{b.Class.GetVariableName()}__{actualNotif.GetVariableName()};");
                });

        // Generate notification handler fields (Task and ValueTask variants)
        var notificationHandlerFields = !useFields
            ? Enumerable.Empty<string>()
            : usedNotifications.SelectMany(n =>
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

        // Dictionary backed cache fields (HandlerCache.Dictionary / HandlerCache.LazyDictionary)
        var cacheFields = cacheMode switch
        {
            HandlerCacheMode.Dictionary => new[]
            {
                "private readonly global::System.Collections.Generic.Dictionary<global::System.Type, object?> _cache = new();",
            },
            HandlerCacheMode.LazyDictionary => new[]
            {
                "private global::System.Collections.Generic.Dictionary<global::System.Type, object?>? _cache;",
            },
            _ => Array.Empty<string>(),
        };

        // Generate Task Send method switch cases
        var taskSendCases = actualSendCases.Select(c =>
        {
            var handlerIsValueTask = handlers.FirstOrDefault(h => h.TRequest.Equals(c.ActualRequest, SymbolEqualityComparer.Default)).IsValueTask;
            return GenerateTaskSendCase(c.ActualRequest, c.Request, handlerIsValueTask, className, cacheMode);
        });

        // Generate ValueTask Send method switch cases
        var valueSendCases = actualSendCases.Select(c =>
        {
            var handlerIsValueTask = handlers.FirstOrDefault(h => h.TRequest.Equals(c.ActualRequest, SymbolEqualityComparer.Default)).IsValueTask;
            return GenerateValueSendCase(c.ActualRequest, c.Request, handlerIsValueTask, className, cacheMode);
        });

        // Generate behavior chain methods for requests
        var behaviorMethods = actualRequestBehaviors
            .Select(r => TryGenerateBehaviorMethod(handlers, r, cacheMode))
            .Where(m => m != null);

        // Generate Task Publish method switch cases (Task-based notification handlers only)
        var taskPublishCases = actualPublishCases
            .Select(n => GenerateTaskPublishCase(n.ActualNotification, n.Notification, notificationHandlers, notificationBehaviors, cacheMode))
            .Where(c => c != null);

        // Generate ValueTask Publish method switch cases (ValueTask-based notification handlers only)
        var valuePublishCases = actualPublishCases
            .Select(n => GenerateValuePublishCase(n.ActualNotification, n.Notification, notificationHandlers, notificationBehaviors, cacheMode))
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
               
                   {{string.Join("\n    ", cacheFields.Concat(handlerFields).Concat(behaviorFields).Concat(notificationBehaviorFields).Concat(notificationHandlerFields))}}
               
                   private readonly {{serviceProviderType}} _svc;
               
                   #endregion
               
                   #region Constructor
               
                   public {{className}}({{serviceProviderType}} serviceProvider)
                   {
                       _svc = serviceProvider;
                   }
               
                   #endregion
               
                   public static (global::System.Collections.Generic.IReadOnlyList<global::System.Type> RequestHandlerTypes, global::System.Collections.Generic.IReadOnlyList<(global::System.Type NotificationType, global::System.Collections.Generic.IReadOnlyList<global::System.Type> HandlerTypes)> NotificationTypes, global::System.Collections.Generic.IReadOnlyList<global::System.Type> PipelineBehaviorTypes) KnownTypes
                   {
                       get { return (SwitchMediatorKnownTypes.RequestHandlerTypes, SwitchMediatorKnownTypes.NotificationTypes, SwitchMediatorKnownTypes.PipelineBehaviorTypes); }
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
               
                   {{cacheAccessor}}
               
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

    /// <summary>
    /// How a component is resolved at the call sites: a field slot (HandlerCache.Fields) or a
    /// typed lookup through the cache accessor (every other mode).
    /// </summary>
    private static string CacheCall(HandlerCacheMode cacheMode, string fieldExpression, string typeExpression) =>
        cacheMode == HandlerCacheMode.Fields ? $"Get(ref {fieldExpression})" : $"Get<{typeExpression}>()";

    private static string GenerateTaskSendCase(
        ITypeSymbol actualHandler,
        (INamedTypeSymbol Class, ITypeSymbol TResponse) request,
        bool handlerIsValueTask,
        string className,
        HandlerCacheMode cacheMode) =>
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
        string className,
        HandlerCacheMode cacheMode) =>
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
        List<((ITypeSymbol Notification, ITypeSymbol ActualNotification) NotificationInfo, List<(INamedTypeSymbol Class, ITypeSymbol TNotification, IReadOnlyList<ITypeParameterSymbol> TypeParameters, bool IsValueTask)> Behaviors)> notificationBehaviors,
        HandlerCacheMode cacheMode)
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
            $"var {b.Class.GetVariableName()}__{notificationName} = instance.{CacheCall(cacheMode, $"instance._{b.Class.GetVariableName()}__{notificationName}", $"{b.Class.ToDisplayString(_fqn).DropGenerics()}<{notificationType}>")};");

        var handlersResolve = $"instance.{CacheCall(cacheMode, $"instance._{actualNotification.GetVariableName()}__Handlers", "global::System.Collections.Generic.IEnumerable<global::Mediator.Switch.INotificationHandler<" + notificationType + ">>")}";
        var typedVar = "typedNotification";
        var coreCall = $"handler.Handle({typedVar}, cancellationToken)";
        var chain = BehaviorChainBuilder.BuildNotification(taskBehaviors, notificationName, typedVar, coreCall);

        return $$"""
                      ( // case {{notification}}:
                          typeof({{notification.ToDisplayString(_fqn)}}), async (instance, notification, cancellationToken) =>
                          {
                              var handlers = {{handlersResolve}};
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
        List<((ITypeSymbol Notification, ITypeSymbol ActualNotification) NotificationInfo, List<(INamedTypeSymbol Class, ITypeSymbol TNotification, IReadOnlyList<ITypeParameterSymbol> TypeParameters, bool IsValueTask)> Behaviors)> notificationBehaviors,
        HandlerCacheMode cacheMode)
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
            $"var {b.Class.GetVariableName()}__{notificationName} = instance.{CacheCall(cacheMode, $"instance._{b.Class.GetVariableName()}__{notificationName}", $"{b.Class.ToDisplayString(_fqn).DropGenerics()}<{notificationType}>")};");

        var handlersResolve = $"instance.{CacheCall(cacheMode, $"instance._{actualNotification.GetVariableName()}__ValueHandlers", "global::System.Collections.Generic.IEnumerable<global::Mediator.Switch.IValueNotificationHandler<" + notificationType + ">>")}";

        var typedVar = "typedNotification";
        var coreCall = $"handler.Handle({typedVar}, cancellationToken)";
        var chain = BehaviorChainBuilder.BuildNotification(valueBehaviors, notificationName, typedVar, coreCall);

        return $$"""
                      ( // case {{notification}}:
                          typeof({{notification.ToDisplayString(_fqn)}}), async (instance, notification, cancellationToken) =>
                          {
                              var handlers = {{handlersResolve}};
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
        ((INamedTypeSymbol Class, ITypeSymbol TResponse) Request, List<(INamedTypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters, bool IsValueTask)> Behaviors) r,
        HandlerCacheMode cacheMode)
    {
        var (request, applicableBehaviors) = r;
        var handler = handlers.FirstOrDefault(h => h.TRequest.Equals(request.Class, SymbolEqualityComparer.Default));
        if (handler == default) return null;
        var requestName = request.Class.GetVariableName();
        var chainVars = applicableBehaviors.Select(behavior => $"var {behavior.Class.GetVariableName()}__{requestName} = {CacheCall(cacheMode, $"_{behavior.Class.GetVariableName()}__{requestName}", $"{behavior.Class.ToDisplayString(_fqn).DropGenerics()}<{request.Class.ToDisplayString(_fqn)}, {behavior.TResponse.ToDisplayString(_fqn)}>")};");
        var coreVar = $"var {handler.Class.GetVariableName()} = {CacheCall(cacheMode, $"_{handler.Class.GetVariableName()}", handler.Class.ToDisplayString(_fqn))};";

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

    private static string Normalize(string code) =>
        string.Join("\n",
            code.Replace("\r\n", "\n")
                .Split('\n')
                .Select(line => line.TrimEnd()));
}