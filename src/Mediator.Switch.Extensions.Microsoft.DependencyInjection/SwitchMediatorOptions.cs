using Microsoft.Extensions.DependencyInjection;

namespace Mediator.Switch.Extensions.Microsoft.DependencyInjection;

public class SwitchMediatorOptions
{
    /// <summary>
    /// Specifies SwitchMediator's pre-discovered types. These are the types that are discovered at compile-type, discovered by SwitchMediator.
    /// </summary>
    public (
        IReadOnlyList<Type> RequestHandlerTypes, 
        IReadOnlyList<(Type NotificationType, IReadOnlyList<Type> HandlerTypes)> NotificationTypes, 
        IReadOnlyList<Type> PipelineBehaviorTypes)
        KnownTypes { get; set; }
    
    /// <summary>
    /// The default lifetime for the services registered by the mediator.
    /// </summary>
    public ServiceLifetime ServiceLifetime { get; set; } = ServiceLifetime.Scoped;

    /// <summary>
    /// Validates when the application starts that every handler and behavior the mediator can dispatch
    /// can actually be resolved from the container, and fails with the list of the ones that cannot.
    /// Off by default.
    /// The container cannot validate the mediator when it builds its graph, because its components are
    /// registered through factories (generated registrations), so a missing dependency would otherwise
    /// surface on the first send of that request. The validation runs in an <c>IHostedService</c>, so it
    /// needs a host, and it instantiates every component once: use it in development and testing.
    /// </summary>
    public bool ValidateOnStart { get; set; }
    
    /// <summary>
    /// Specify explicit notification handler ordering. Any other handlers not specified will assume lower priority.
    /// </summary>
    /// <param name="handlerTypes">Handler types in the preferred order.</param>
    /// <typeparam name="TNotification"></typeparam>
    /// <returns></returns>
    public SwitchMediatorOptions OrderNotificationHandlers<TNotification>(params Type[] handlerTypes)
        where TNotification : INotification
    {
        if (handlerTypes.Length == 0)
        {
            return this;
        }

        if (handlerTypes.Distinct().Count() != handlerTypes.Length)
        {
            throw new ArgumentException("Duplicate notification handler types are not allowed.", nameof(handlerTypes));
        }
        
        foreach (var handlerType in handlerTypes)
        {
            if (!typeof(INotificationHandler<TNotification>).IsAssignableFrom(handlerType) &&
                !typeof(IValueNotificationHandler<TNotification>).IsAssignableFrom(handlerType))
            {
                throw new ArgumentException($"Type {handlerType.Name} does not implement INotificationHandler<{typeof(TNotification).Name}> or IValueNotificationHandler<{typeof(TNotification).Name}>");
            }
        }

        OrderedNotificationHandlers.Add(typeof(TNotification), handlerTypes);
        return this;
    }

    internal Dictionary<Type, Type[]> OrderedNotificationHandlers { get; } = new();
}