using Mediator.Switch;
using System;
using System.Threading.Tasks;

namespace Test.Notifications;

// The Notification
public class OrderCreatedEvent : INotification
{
    public int OrderId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

// First Handler
public class OrderEmailNotifier : INotificationHandler<OrderCreatedEvent>
{
    public Task Handle(OrderCreatedEvent notification)
    {
        Console.WriteLine($"Email Handler: Order {notification.OrderId} created at {notification.Timestamp}.");
        return Task.CompletedTask;
    }
}

// Second Handler
public class OrderAnalyticsTracker : INotificationHandler<OrderCreatedEvent>
{
    public Task Handle(OrderCreatedEvent notification)
    {
        Console.WriteLine($"Analytics Handler: Tracking order {notification.OrderId}.");
        return Task.CompletedTask;
    }
}

// Unrelated Request (to ensure notifications don't interfere with request handling)
public class SimpleRequest : IRequest<bool>;
public class SimpleRequestHandler : IRequestHandler<SimpleRequest, bool>
{
    public Task<bool> Handle(SimpleRequest request) => Task.FromResult(true);
}