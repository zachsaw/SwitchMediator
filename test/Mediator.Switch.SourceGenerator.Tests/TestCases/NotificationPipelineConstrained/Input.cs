using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.NotificationPipelineConstrained;

[SwitchMediator]
public partial class TestMediator;

public interface IAuditableNotification { }

// Implements marker interface
public class UserCreated : INotification, IAuditableNotification
{
    public int UserId { get; set; }
}

// Does NOT implement marker interface
public class SystemTick : INotification { }

public class UserCreatedHandler : INotificationHandler<UserCreated>
{
    public Task Handle(UserCreated notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public class SystemTickHandler : INotificationHandler<SystemTick>
{
    public Task Handle(SystemTick notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

// Behavior constrained to IAuditableNotification
// Should only apply to UserCreated, not SystemTick
public class AuditBehavior<TNotification> : INotificationPipelineBehavior<TNotification>
    where TNotification : IAuditableNotification
{
    public async Task Handle(TNotification notification, NotificationHandlerDelegate next, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Auditing {notification.GetType().Name}");
        await next(cancellationToken);
    }
}