using Mediator.Switch.Extensions.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Mediator.Switch.Tests;

public class MediatorNotificationOrderingTests : IDisposable
{
    private ServiceProvider? _serviceProvider;
    private IServiceScope? _scope;

    private (IPublisher publisher, NotificationTracker tracker) SetupTestScope(
        Action<SwitchMediatorOptions>? configureMediator = null)
    {
        var setupResult = MediatorTestSetup.Setup(configureMediator: configureMediator);

        _serviceProvider = setupResult.ServiceProvider;
        _scope = setupResult.Scope;

        return (setupResult.Publisher, setupResult.Tracker);
    }

    [Fact]
    public async Task Publish_WhenNoOrderSpecified_RunsAllHandlersInUnknownOrder()
    {
        // Arrange
        var (publisher, tracker) = SetupTestScope();
        var notification = new UserLoggedInEvent(1);

        // Act
        await publisher.Publish(notification);

        // Assert
        Assert.Equal(2, tracker.ExecutionOrder.Count);
        var executedHandlers = tracker.ExecutionOrder.ToHashSet();
        Assert.Contains(nameof(TestUserLoggedInLogger), executedHandlers);
        Assert.Contains(nameof(TestUserLoggedInAnalytics), executedHandlers);
    }

    [Fact]
    public async Task Publish_WhenPartialOrderSpecified_RunsOrderedFirstThenRemaining()
    {
        // Arrange
        var (publisher, tracker) = SetupTestScope(op =>
        {
            op.OrderNotificationHandlers<UserLoggedInEvent>(
                typeof(TestUserLoggedInLogger)
            );
        });
        var notification = new UserLoggedInEvent(2);

        // Act
        await publisher.Publish(notification);

        // Assert
        Assert.Equal(2, tracker.ExecutionOrder.Count);
        Assert.True(tracker.ExecutionOrder.TryDequeue(out var firstHandler));
        Assert.True(tracker.ExecutionOrder.TryDequeue(out var secondHandler));
        Assert.Equal(nameof(TestUserLoggedInLogger), firstHandler);
        Assert.Equal(nameof(TestUserLoggedInAnalytics), secondHandler);
    }

    [Fact]
    public async Task Publish_WhenFullOrderSpecified_RunsHandlersInSpecifiedOrder()
    {
        // Arrange
        var (publisher, tracker) = SetupTestScope(op =>
        {
            op.OrderNotificationHandlers<UserLoggedInEvent>(
                typeof(TestUserLoggedInAnalytics),
                typeof(TestUserLoggedInLogger)
            );
        });
        var notification = new UserLoggedInEvent(3);

        // Act
        await publisher.Publish(notification);

        // Assert
        Assert.Equal(2, tracker.ExecutionOrder.Count);
        Assert.True(tracker.ExecutionOrder.TryDequeue(out var firstHandler));
        Assert.True(tracker.ExecutionOrder.TryDequeue(out var secondHandler));
        Assert.Equal(nameof(TestUserLoggedInAnalytics), firstHandler);
        Assert.Equal(nameof(TestUserLoggedInLogger), secondHandler);
    }

    [Fact]
    public void AddMediator_WhenDuplicateHandlerSpecifiedInOrder_ThrowsArgumentException()
    {
        // Arrange
        // Setup action now calls the shared setup helper
        static void SetupAction() =>
            MediatorTestSetup.Setup(op =>
            {
                op.OrderNotificationHandlers<UserLoggedInEvent>(
                    typeof(TestUserLoggedInLogger),
                    typeof(TestUserLoggedInAnalytics),
                    typeof(TestUserLoggedInLogger) // Duplicate!
                );
            });

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>((Action) SetupAction);
        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _scope?.Dispose();
        _serviceProvider?.Dispose();
        GC.SuppressFinalize(this);
    }
}