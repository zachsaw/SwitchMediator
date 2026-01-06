using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.NotificationPipeline;

[SwitchMediator]
public partial class TestMediator;

// Notification
public class Alert : INotification
{
    public string Message { get; set; } = "";
}

// Handler 1
public class EmailAlertHandler : INotificationHandler<Alert>
{
    public Task Handle(Alert notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Email: {notification.Message}");
        return Task.CompletedTask;
    }
}

// Handler 2
public class SmsAlertHandler : INotificationHandler<Alert>
{
    public Task Handle(Alert notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"SMS: {notification.Message}");
        throw new Exception("SMS Gateway Failed"); // To test exception handling behavior
    }
}

// Behavior 1: Outer wrapper (Order 1)
[PipelineBehaviorOrder(1)]
public class LoggingNotificationBehavior<TNotification> : INotificationPipelineBehavior<TNotification>
    where TNotification : notnull
{
    public async Task Handle(TNotification notification, NotificationHandlerDelegate next, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[Log Start] {notification.GetType().Name}");
        await next(cancellationToken);
        Console.WriteLine($"[Log End] {notification.GetType().Name}");
    }
}

// Behavior 2: Inner wrapper (Order 2) - Swallows exceptions
[PipelineBehaviorOrder(2)]
public class ExceptionHandlingBehavior<TNotification> : INotificationPipelineBehavior<TNotification>
    where TNotification : notnull
{
    public async Task Handle(TNotification notification, NotificationHandlerDelegate next, CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine("[Try]");
            await next(cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Caught] {ex.Message}");
        }
    }
}