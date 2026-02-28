using Microsoft.Extensions.DependencyInjection;

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

        services.Add(new ServiceDescriptor(typeof(ISwitchMediatorServiceProvider), typeof(MicrosoftDependencyInjectionServiceProvider), options.ServiceLifetime));

        services.Add(new ServiceDescriptor(typeof(IMediator), typeof(TSwitchMediator), options.ServiceLifetime));
        services.Add(new ServiceDescriptor(typeof(ISender), sp => sp.GetRequiredService<IMediator>(), options.ServiceLifetime));
        services.Add(new ServiceDescriptor(typeof(IPublisher), sp => sp.GetRequiredService<IMediator>(), options.ServiceLifetime));

        // Register IValueMediator if the mediator implements it
        if (typeof(IValueMediator).IsAssignableFrom(typeof(TSwitchMediator)))
        {
            services.Add(new ServiceDescriptor(typeof(IValueMediator), sp => (IValueMediator)sp.GetRequiredService<IMediator>(), options.ServiceLifetime));
            services.Add(new ServiceDescriptor(typeof(IValueSender), sp => sp.GetRequiredService<IValueMediator>(), options.ServiceLifetime));
            services.Add(new ServiceDescriptor(typeof(IValuePublisher), sp => sp.GetRequiredService<IValueMediator>(), options.ServiceLifetime));
        }

        if (options.KnownTypes != default)
        {
            RegisterRequestHandlers(services, options.KnownTypes.RequestHandlerTypes, options);
            RegisterNotificationHandlers(services,
                options.KnownTypes.NotificationTypes.Select(n => (n.NotificationType, n.HandlerTypes.ToArray())),
                options);
            RegisterPipelineBehaviors(services, options.KnownTypes.PipelineBehaviorTypes, options);
        }

        return services;
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