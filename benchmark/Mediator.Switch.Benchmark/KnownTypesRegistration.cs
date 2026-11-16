using Mediator.Switch.Extensions.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Mediator.Switch.Benchmark;

// The registration Mediator.Switch.Extensions.Microsoft.DependencyInjection 3.3.0 performs from KnownTypes (type
// registrations, notification service types built with MakeGenericType), kept here as the benchmark's baseline.
internal static class KnownTypesRegistration
{
    public static IServiceCollection AddMediatorFromKnownTypes<TSwitchMediator>(
        this IServiceCollection services,
        (IReadOnlyList<Type> RequestHandlerTypes, IReadOnlyList<(Type NotificationType, IReadOnlyList<Type> HandlerTypes)> NotificationTypes, IReadOnlyList<Type> PipelineBehaviorTypes) knownTypes,
        ServiceLifetime lifetime)
        where TSwitchMediator : class, IMediator
    {
        services.Add(new ServiceDescriptor(typeof(ISwitchMediatorServiceProvider), typeof(MicrosoftDependencyInjectionServiceProvider), lifetime));
        services.Add(new ServiceDescriptor(typeof(IMediator), typeof(TSwitchMediator), lifetime));
        services.Add(new ServiceDescriptor(typeof(ISender), sp => sp.GetRequiredService<IMediator>(), lifetime));
        services.Add(new ServiceDescriptor(typeof(IPublisher), sp => sp.GetRequiredService<IMediator>(), lifetime));
        if (typeof(IValueMediator).IsAssignableFrom(typeof(TSwitchMediator)))
        {
            services.Add(new ServiceDescriptor(typeof(IValueMediator), sp => (IValueMediator)sp.GetRequiredService<IMediator>(), lifetime));
            services.Add(new ServiceDescriptor(typeof(IValueSender), sp => (IValueSender)sp.GetRequiredService<IMediator>(), lifetime));
            services.Add(new ServiceDescriptor(typeof(IValuePublisher), sp => (IValuePublisher)sp.GetRequiredService<IMediator>(), lifetime));
        }
        foreach (var handlerType in knownTypes.RequestHandlerTypes)
        {
            services.Add(new ServiceDescriptor(handlerType, handlerType, lifetime));
        }
        foreach (var (notificationType, handlerTypes) in knownTypes.NotificationTypes)
        {
            var valueHandler = typeof(IValueNotificationHandler<>).MakeGenericType(notificationType);
            var taskHandler = typeof(INotificationHandler<>).MakeGenericType(notificationType);
            foreach (var handlerType in handlerTypes)
            {
                services.Add(new ServiceDescriptor(valueHandler.IsAssignableFrom(handlerType) ? valueHandler : taskHandler, handlerType, lifetime));
            }
        }
        foreach (var behaviorType in knownTypes.PipelineBehaviorTypes)
        {
            services.Add(new ServiceDescriptor(behaviorType, behaviorType, lifetime));
        }
        return services;
    }
}
