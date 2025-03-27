using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Mediator.Switch.Extensions.Microsoft.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddScoped<TSwitchMediator>(this IServiceCollection services, params Assembly[] assembliesToScan)
            where TSwitchMediator : class, IMediator
        {
            services.AddScoped<IMediator, TSwitchMediator>();
            services.AddScoped<ISender>(sp => sp.GetRequiredService<IMediator>());
            services.AddScoped<IPublisher>(sp => sp.GetRequiredService<IMediator>());

            var allTypes = assembliesToScan
                .Where(a => a != null)
                .SelectMany(a => a.GetTypes())
                .ToArray();

            // Register Handlers
            var handlerTypes = allTypes
                .Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces().Any(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
                .ToList();

            foreach (var handlerType in handlerTypes)
            {
                services.AddScoped(handlerType);
            }

            // Register Notification Handlers (without explicit ordering initially)
            var notificationHandlerTypes = allTypes
                .Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces().Any(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>)))
                .ToList();

            foreach (var handlerType in notificationHandlerTypes)
            {
                services.AddScoped(handlerType);
            }

            // Register Pipeline Behaviors
            var pipelineBehaviorTypes = allTypes
                .Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces().Any(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>)))
                .ToList();

            foreach (var behaviorType in pipelineBehaviorTypes)
            {
                services.AddScoped(behaviorType);
            }

            return services;
        }

        public static IServiceCollection OrderNotificationHandlers<TNotification>(this IServiceCollection services, params Type[] handlerTypes)
            where TNotification : INotification
        {
            services.AddScoped<IEnumerable<INotificationHandler<TNotification>>>(sp =>
            {
                return handlerTypes
                    .Select(handlerType => (INotificationHandler<TNotification>)sp.GetRequiredService(handlerType))
                    .ToList();
            });
            return services;
        }
    }
}