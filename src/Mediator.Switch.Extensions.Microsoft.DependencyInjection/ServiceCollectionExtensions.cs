using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Mediator.Switch.Extensions.Microsoft.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddMediator<TSwitchMediator>(this IServiceCollection services, params Assembly[] assembliesToScan)
            where TSwitchMediator : class, IMediator
        {
            return AddMediator<TSwitchMediator>(services, op =>
            {
                op.TargetAssemblies = assembliesToScan;
                op.ServiceLifetime = ServiceLifetime.Scoped;
            });
        }

        public static IServiceCollection AddMediator<TSwitchMediator>(this IServiceCollection services, Action<SwitchMediatorOptions>? configure)
            where TSwitchMediator : class, IMediator
        {
            var options = new SwitchMediatorOptions();
            
            if (configure != null)
                configure(options);
            
            services.Add(new ServiceDescriptor(typeof(IMediator), typeof(TSwitchMediator), options.ServiceLifetime));
            services.Add(new ServiceDescriptor(typeof(ISender), sp => sp.GetRequiredService<IMediator>(), options.ServiceLifetime));
            services.Add(new ServiceDescriptor(typeof(IPublisher), sp => sp.GetRequiredService<IMediator>(), options.ServiceLifetime));

            // get all types from the target assemblies
            var allTypes = options.TargetAssemblies.Where(assembly => assembly != null)
                .SelectMany(assembly => assembly.GetTypes())
                .ToArray();

            // Register Handlers
            var handlerTypes = allTypes
                .Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces().Any(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
                .ToList();

            foreach (var handlerType in handlerTypes)
            {
                services.Add(new ServiceDescriptor(handlerType, handlerType, options.ServiceLifetime));
            }

            // Register Notification Handlers (without explicit ordering initially)
            var notificationHandlerTypes = allTypes
                .Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces().Any(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>)))
                .ToList();

            foreach (var handlerType in notificationHandlerTypes)
            {
                // Register the concrete type
				services.Add(new ServiceDescriptor(handlerType, handlerType, options.ServiceLifetime));
                
                // Also register against notification handler interfaces
                foreach (var handlerInterface in handlerType.GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>)))
                {
					services.Add(new ServiceDescriptor(handlerInterface, sp => sp.GetRequiredService(handlerType), options.ServiceLifetime));
                }
            }

            // Register Pipeline Behaviors
            var pipelineBehaviorTypes = allTypes
                .Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces().Any(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>)))
                .ToList();

            foreach (var behaviorType in pipelineBehaviorTypes)
            {
                services.Add(new ServiceDescriptor(behaviorType, behaviorType, options.ServiceLifetime));
            }

            return services;
        }

        public static IServiceCollection OrderNotificationHandlers<TNotification>(this IServiceCollection services, params Type[] handlerTypes)
            where TNotification : INotification
        {
            services.Add(new ServiceDescriptor(typeof(IEnumerable<INotificationHandler<TNotification>>),
                sp => handlerTypes.Select(handlerType
                    => (INotificationHandler<TNotification>)sp.GetRequiredService(handlerType)).ToList(),
                ServiceLifetime.Scoped));
          
            return services;
        }
    }
}