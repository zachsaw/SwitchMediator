using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Mediator.Switch.Extensions.Microsoft.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers SwitchMediator services using pre-discovered types, avoiding runtime assembly scanning.
    /// </summary>
    /// <typeparam name="TSwitchMediator">The concrete implementation type of IMediator to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceLifetime">The service lifetime for the mediator and its handlers/behaviors.</param>
    /// <param name="knownTypes">A tuple containing pre-discovered lists of request handler, notification handler, and pipeline behavior types. Typically provided by a source generator.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMediator<TSwitchMediator>(this IServiceCollection services, ServiceLifetime serviceLifetime,
        (IReadOnlyList<Type> RequestHandlerTypes, IReadOnlyList<(Type NotificationType, IReadOnlyList<Type> HandlerTypes)> NotificationHandlerTypes, IReadOnlyList<Type> PipelineBehaviorTypes) knownTypes)
        where TSwitchMediator : class, IMediator =>
        AddMediator<TSwitchMediator>(services, op =>
        {
            op.KnownTypes = knownTypes;
            op.ServiceLifetime = serviceLifetime;
        });

    /// <summary>
    /// Registers SwitchMediator services, allowing detailed configuration via the <paramref name="configure"/> action.
    /// This is the primary configuration method, enabling specification of service lifetime, assemblies to scan,
    /// or pre-discovered known types, and ordered notification handlers.
    /// </summary>
    /// <typeparam name="TSwitchMediator">The concrete implementation type of IMediator to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional action to configure SwitchMediator options, such as service lifetime, assemblies to scan, pre-discovered known types, or ordered notification handlers.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMediator<TSwitchMediator>(this IServiceCollection services, Action<SwitchMediatorOptions>? configure)
        where TSwitchMediator : class, IMediator
    {
        var options = new SwitchMediatorOptions();

        configure?.Invoke(options);

        // A mediator generated with registrations is registered through their factories, so the container creates
        // nothing by reflection (and Native AOT trims nothing it needs). Older generated mediators use KnownTypes.
        var components = new List<(Type Service, Type Implementation)>();
        var registrar = new ServiceCollectionRegistrar(services, options.ServiceLifetime, options.OrderedNotificationHandlers, components);
        if (SwitchMediatorRegistrations.TryRegister<TSwitchMediator>(registrar))
        {
            registrar.Complete();
            services.Add(new ServiceDescriptor(typeof(ISwitchMediatorServiceProvider), sp => new MicrosoftDependencyInjectionServiceProvider(sp), options.ServiceLifetime));
            AddGraphValidation(services, options, components);
            return services;
        }

        services.Add(new ServiceDescriptor(typeof(ISwitchMediatorServiceProvider), typeof(MicrosoftDependencyInjectionServiceProvider), options.ServiceLifetime));

        services.Add(new ServiceDescriptor(typeof(IMediator), typeof(TSwitchMediator), options.ServiceLifetime));
        services.Add(new ServiceDescriptor(typeof(ISender), sp => sp.GetRequiredService<IMediator>(), options.ServiceLifetime));
        services.Add(new ServiceDescriptor(typeof(IPublisher), sp => sp.GetRequiredService<IMediator>(), options.ServiceLifetime));

        // Register IValueMediator if the mediator implements it
        if (typeof(IValueMediator).IsAssignableFrom(typeof(TSwitchMediator)))
        {
            services.Add(new ServiceDescriptor(typeof(IValueMediator), sp => (IValueMediator)sp.GetRequiredService<IMediator>(), options.ServiceLifetime));
            services.Add(new ServiceDescriptor(typeof(IValueSender), sp => (IValueSender)sp.GetRequiredService<IMediator>(), options.ServiceLifetime));
            services.Add(new ServiceDescriptor(typeof(IValuePublisher), sp => (IValuePublisher)sp.GetRequiredService<IMediator>(), options.ServiceLifetime));
        }

        if (options.KnownTypes != default)
        {
            RegisterRequestHandlers(services, options.KnownTypes.RequestHandlerTypes, options);
            RegisterNotificationHandlers(services,
                options.KnownTypes.NotificationTypes.Select(n => (n.NotificationType, n.HandlerTypes.ToArray())),
                options);
            RegisterPipelineBehaviors(services, options.KnownTypes.PipelineBehaviorTypes, options);

            foreach (var handlerType in options.KnownTypes.RequestHandlerTypes)
            {
                components.Add((handlerType, handlerType));
            }

            foreach (var (notificationType, handlerTypes) in options.KnownTypes.NotificationTypes)
            {
                foreach (var handlerType in handlerTypes)
                {
                    var service = typeof(IValueNotificationHandler<>).MakeGenericType(notificationType);
                    components.Add((service.IsAssignableFrom(handlerType) ? service : typeof(INotificationHandler<>).MakeGenericType(notificationType), handlerType));
                }
            }

            foreach (var behaviorType in options.KnownTypes.PipelineBehaviorTypes)
            {
                components.Add((behaviorType, behaviorType));
            }
        }

        AddGraphValidation(services, options, components);

        return services;
    }

    // Adds each generated component to the collection as it arrives. The handlers of a notification given an order
    // are held until its last one arrives, then added in that order; handlers the order does not name follow, in
    // their generated order.
    private sealed class ServiceCollectionRegistrar(
        IServiceCollection services,
        ServiceLifetime lifetime,
        Dictionary<Type, Type[]> order,
        List<(Type Service, Type Implementation)> components) : ISwitchMediatorRegistrar
    {
        private readonly List<(Type Service, Type Implementation, Func<IServiceProvider, object>? Factory)> _held = new();
        private Type? _heldNotification;
        private Type[] _heldOrder = Array.Empty<Type>();

        public void Add(Type serviceType, Type implementationType, Func<IServiceProvider, object>? factory, Type? notificationType)
        {
            components.Add((serviceType, implementationType));

            if (notificationType != _heldNotification)
            {
                Complete();
            }
            if (notificationType != null && order.TryGetValue(notificationType, out var specific))
            {
                _heldNotification = notificationType;
                _heldOrder = specific;
                _held.Add((serviceType, implementationType, factory));
                return;
            }
            Write(serviceType, implementationType, factory);
        }

        public void Complete()
        {
            if (_held.Count > 0)
            {
                foreach (var (service, implementation, factory) in _held.OrderBy(h =>
                             Array.IndexOf(_heldOrder, h.Implementation) is var index and >= 0 ? index : _heldOrder.Length))
                {
                    Write(service, implementation, factory);
                }
                _held.Clear();
            }
            _heldNotification = null;
        }

        private void Write(Type service, Type implementation, Func<IServiceProvider, object>? factory) =>
            services.Add(factory != null
                ? new ServiceDescriptor(service, factory, lifetime)
                : new ServiceDescriptor(service, implementation, lifetime));
    }

    /// <summary>
    /// Registers the startup validation of the mediator graph when the application asks for it.
    /// </summary>
    private static void AddGraphValidation(IServiceCollection services, SwitchMediatorOptions options, List<(Type Service, Type Implementation)> components)
    {
        if (!options.ValidateOnStart || components.Count == 0)
        {
            return;
        }

        services.AddSingleton<IHostedService>(sp => new MediatorGraphValidator(components, sp));
    }

    private static void RegisterRequestHandlers(IServiceCollection services, IEnumerable<Type> requestHandlerTypes, SwitchMediatorOptions options)
    {
        foreach (var handlerType in requestHandlerTypes)
        {
            services.Add(new ServiceDescriptor(handlerType, handlerType, options.ServiceLifetime));
        }
    }

    private static void RegisterNotificationHandlers(
        IServiceCollection services,
        IEnumerable<(Type NotificationType, Type[] HandlerTypes)> notificationTypes,
        SwitchMediatorOptions options)
    {
        foreach (var n in notificationTypes)
        {
            if (options.OrderedNotificationHandlers.TryGetValue(n.NotificationType, out var orderedHandlerTypes))
            {
                Sort(n.HandlerTypes, orderedHandlerTypes);
            }

            var valueNotifHandlerType = typeof(IValueNotificationHandler<>).MakeGenericType(n.NotificationType);
            var taskNotifHandlerType = typeof(INotificationHandler<>).MakeGenericType(n.NotificationType);

            foreach (var handlerType in n.HandlerTypes)
            {
                // Register as IValueNotificationHandler if applicable, otherwise INotificationHandler
                services.Add(new ServiceDescriptor(
                    valueNotifHandlerType.IsAssignableFrom(handlerType) ? valueNotifHandlerType : taskNotifHandlerType,
                    handlerType,
                    options.ServiceLifetime));
            }
        }
    }

    private static void RegisterPipelineBehaviors(IServiceCollection services, IEnumerable<Type> behaviorTypes, SwitchMediatorOptions options)
    {
        foreach (var behaviorType in behaviorTypes)
        {
            services.Add(new ServiceDescriptor(behaviorType, behaviorType, options.ServiceLifetime));
        }
    }

    private static void Sort(Type[] typesToSort, Type[] specificOrder)
    {
        if (specificOrder.Length == 0)
        {
            return;
        }

        Array.Sort(typesToSort, (Comparison<Type>) Comparison);
        return;

        // note: This is going to be more performant than creating a dictionary for lookups given a small number of types
        int Comparison(Type x, Type y)
        {
            var indexX = Array.IndexOf(specificOrder, x);
            var indexY = Array.IndexOf(specificOrder, y);
            var keyX = indexX >= 0 ? indexX : specificOrder.Length;
            var keyY = indexY >= 0 ? indexY : specificOrder.Length;
            return keyX.CompareTo(keyY);
        }
    }
}