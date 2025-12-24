namespace Mediator.Switch.Tests;

public class TestUserLoggedInLogger(NotificationTracker tracker) : INotificationHandler<UserLoggedInEvent>
{
    public Task Handle(UserLoggedInEvent notification, CancellationToken cancellationToken = default)
    {
        tracker.ExecutionOrder.Enqueue(nameof(TestUserLoggedInLogger));
        return Task.CompletedTask;
    }
}