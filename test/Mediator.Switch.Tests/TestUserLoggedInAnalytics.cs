namespace Mediator.Switch.Tests;

public class TestUserLoggedInAnalytics(NotificationTracker tracker) : INotificationHandler<UserLoggedInEvent>
{
    public Task Handle(UserLoggedInEvent notification, CancellationToken cancellationToken = default)
    {
        tracker.ExecutionOrder.Enqueue(nameof(TestUserLoggedInAnalytics));
        return Task.CompletedTask;
    }
}